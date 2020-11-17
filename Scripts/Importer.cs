using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace Siccity.GLTFUtility {
	/// <summary> API used for importing .gltf and .glb files </summary>
	public static class Importer {
		public static void LoadFromFile(string filepath, Action<GameObject, AnimationClip[ ]> onFinished, Format format = Format.AUTO)
		{
			LoadFromFile(filepath, new ImportSettings(), onFinished, format);
		}

		public static void LoadFromFile(string filepath, ImportSettings importSettings, Action<GameObject, AnimationClip[ ]> onFinished, Format format = Format.AUTO)
		{
			if (format == Format.GLB) {
				ImportGLB(filepath, importSettings, onFinished);
			} else if (format == Format.GLTF) {
				ImportGLTF(filepath, importSettings, onFinished);
			} else {
				string extension = Path.GetExtension(filepath).ToLower();
				if (extension == ".glb") ImportGLB(filepath, importSettings, onFinished);
				else if (extension == ".gltf") ImportGLTF(filepath, importSettings, onFinished);
				else {
					Debug.Log("Extension '" + extension + "' not recognized in " + filepath);
				}
			}
		}

		/// <param name="bytes">GLB file is supported</param>
		public static void LoadFromBytes(byte[] bytes, Action<GameObject, AnimationClip[ ]> onFinished, ImportSettings importSettings = null ) {
			if (importSettings == null) importSettings = new ImportSettings();
			ImportGLB(bytes, importSettings, onFinished);
		}

		public static void LoadFromFileAsync(string filepath, ImportSettings importSettings, Action<GameObject, AnimationClip[]> onFinished, Action<float> onProgress = null) {
			string extension = Path.GetExtension(filepath).ToLower();
			if (extension == ".glb") ImportGLBAsync(filepath, importSettings, onFinished, onProgress);
			else if (extension == ".gltf") ImportGLTFAsync(filepath, importSettings, onFinished, onProgress);
			else {
				Debug.Log("Extension '" + extension + "' not recognized in " + filepath);
				onFinished(null, null);
			}
		}

#region GLB
		private static void ImportGLB(string filepath, ImportSettings importSettings, Action<GameObject, AnimationClip[ ]> onFinished ) {
			FileStream stream = File.OpenRead(filepath);
			long binChunkStart;
			string json = GetGLBJson(stream, out binChunkStart);
			GLTFObject gltfObject = JsonConvert.DeserializeObject<GLTFObject>(json);
			StaticCoroutine.Start( gltfObject.LoadInternal(filepath, null, binChunkStart, importSettings, onFinished) );
		}

		private static void ImportGLB(byte[] bytes, ImportSettings importSettings, Action<GameObject, AnimationClip[ ]> onFinished ) {
			Stream stream = new MemoryStream(bytes);
			long binChunkStart;
			string json = GetGLBJson(stream, out binChunkStart);
			GLTFObject gltfObject = JsonConvert.DeserializeObject<GLTFObject>(json);
			StaticCoroutine.Start( gltfObject.LoadInternal(null, bytes, binChunkStart, importSettings, onFinished ) );
		}

		public static void ImportGLBAsync(string filepath, ImportSettings importSettings, Action<GameObject, AnimationClip[]> onFinished, Action<float> onProgress = null) {
			FileStream stream = File.OpenRead(filepath);
			long binChunkStart;
			string json = GetGLBJson(stream, out binChunkStart);
			StaticCoroutine.Start( LoadAsync(json, filepath, null, binChunkStart, importSettings, onFinished, onProgress) );
		}

		public static void ImportGLBAsync(byte[] bytes, ImportSettings importSettings, Action<GameObject, AnimationClip[]> onFinished, Action<float> onProgress = null) {
			Stream stream = new MemoryStream(bytes);
			long binChunkStart;
			string json = GetGLBJson(stream, out binChunkStart);
			StaticCoroutine.Start( LoadAsync(json, null, bytes, binChunkStart, importSettings, onFinished, onProgress) );
		}

		private static string GetGLBJson(Stream stream, out long binChunkStart) {
			byte[] buffer = new byte[12];
			stream.Read(buffer, 0, 12);
			// 12 byte header
			// 0-4  - magic = "glTF"
			// 4-8  - version = 2
			// 8-12 - length = total length of glb, including Header and all Chunks, in bytes.
			string magic = Encoding.Default.GetString(buffer, 0, 4);
			if (magic != "glTF") {
				Debug.LogWarning("Input does not look like a .glb file");
				binChunkStart = 0;
				return null;
			}
			uint version = System.BitConverter.ToUInt32(buffer, 4);
			if (version != 2) {
				Debug.LogWarning("Importer does not support gltf version " + version);
				binChunkStart = 0;
				return null;
			}
			// What do we even need the length for.
			//uint length = System.BitConverter.ToUInt32(bytes, 8);

			// Chunk 0 (json)
			// 0-4  - chunkLength = total length of the chunkData
			// 4-8  - chunkType = "JSON"
			// 8-[chunkLength+8] - chunkData = json data.
			stream.Read(buffer, 0, 8);
			uint chunkLength = System.BitConverter.ToUInt32(buffer, 0);
			TextReader reader = new StreamReader(stream);
			char[] jsonChars = new char[chunkLength];
			reader.Read(jsonChars, 0, (int) chunkLength);
			string json = new string(jsonChars);

			// Chunk
			binChunkStart = chunkLength + 20;
			stream.Close();

			// Return json
			return json;
		}
#endregion

		private static void ImportGLTF(string filepath, ImportSettings importSettings, Action<GameObject, AnimationClip[]> onFinished )
		{
			string json = File.ReadAllText(filepath);

			// Parse json
			GLTFObject gltfObject = JsonConvert.DeserializeObject<GLTFObject>(json);
			StaticCoroutine.Start( gltfObject.LoadInternal(filepath, null, 0, importSettings, onFinished) );
		}

		public static void ImportGLTFAsync(string filepath, ImportSettings importSettings, Action<GameObject, AnimationClip[]> onFinished, Action<float> onProgress = null) {
			string json = File.ReadAllText(filepath);

			// Parse json
			StaticCoroutine.Start( LoadAsync(json, filepath, null, 0, importSettings, onFinished, onProgress) );
		}

		public abstract class ImportTask<TReturn> : ImportTask {
			public TReturn Result;

			/// <summary> Constructor. Sets waitFor which ensures ImportTasks are completed before running. </summary>
			public ImportTask(params ImportTask[] waitFor) : base(waitFor) { }

			/// <summary> Runs task followed by OnCompleted </summary>
			public IEnumerator RunSynchronously() {
				task.RunSynchronously();
				yield return StaticCoroutine.Start( OnCoroutine() );
			}
		}

		public abstract class ImportTask {
			public Task task;
			public readonly ImportTask[] waitFor;
			public bool IsReady { get { return waitFor.All(x => x.IsCompleted); } }
			public bool IsCompleted { get; protected set; }

			/// <summary> Constructor. Sets waitFor which ensures ImportTasks are completed before running. </summary>
			public ImportTask(params ImportTask[] waitFor) {
				IsCompleted = false;
				this.waitFor = waitFor;
			}

			public virtual IEnumerator OnCoroutine(Action<float> onProgress = null) {
				IsCompleted = true;
				yield break;
			}
		}

#region Sync
		private static IEnumerator LoadInternal(this GLTFObject gltfObject, string filepath, byte[] bytefile, long binChunkStart, ImportSettings importSettings, Action<GameObject, AnimationClip[ ]> onFinished )
		{
			CheckExtensions(gltfObject);

			// directory root is sometimes used for loading buffers from containing file, or local images
			string directoryRoot = filepath != null ? Directory.GetParent(filepath).ToString() + "/" : null;

			importSettings.shaderOverrides.CacheDefaultShaders();

			// Import tasks synchronously
			GLTFBuffer.ImportTask bufferTask = new GLTFBuffer.ImportTask(gltfObject.buffers, filepath, bytefile, binChunkStart);
			yield return StaticCoroutine.Start( bufferTask.RunSynchronously() );
			GLTFBufferView.ImportTask bufferViewTask = new GLTFBufferView.ImportTask(gltfObject.bufferViews, bufferTask);
			yield return StaticCoroutine.Start( bufferViewTask.RunSynchronously() );
			GLTFAccessor.ImportTask accessorTask = new GLTFAccessor.ImportTask(gltfObject.accessors, bufferViewTask);
			yield return StaticCoroutine.Start( accessorTask.RunSynchronously() );
			GLTFImage.ImportTask imageTask = new GLTFImage.ImportTask(gltfObject.images, directoryRoot, bufferViewTask);
			yield return StaticCoroutine.Start( imageTask.RunSynchronously() );
			GLTFTexture.ImportTask textureTask = new GLTFTexture.ImportTask(gltfObject.textures, imageTask);
			yield return StaticCoroutine.Start( textureTask.RunSynchronously() );
			GLTFMaterial.ImportTask materialTask = new GLTFMaterial.ImportTask(gltfObject.materials, textureTask, importSettings);
			yield return StaticCoroutine.Start( materialTask.RunSynchronously() );
			GLTFMesh.ImportTask meshTask = new GLTFMesh.ImportTask(gltfObject.meshes, accessorTask, bufferViewTask, materialTask, importSettings);
			yield return StaticCoroutine.Start( meshTask.RunSynchronously() );
			GLTFSkin.ImportTask skinTask = new GLTFSkin.ImportTask(gltfObject.skins, accessorTask);
			yield return StaticCoroutine.Start( skinTask.RunSynchronously() );
			GLTFNode.ImportTask nodeTask = new GLTFNode.ImportTask(gltfObject.nodes, meshTask, skinTask, gltfObject.cameras);
			yield return StaticCoroutine.Start( nodeTask.RunSynchronously() );

			GLTFAnimation.ImportResult[] animationResult = gltfObject.animations.Import(accessorTask.Result, nodeTask.Result, importSettings);
			AnimationClip[] animations = null;
			if (animationResult != null) animations = animationResult.Select(x => x.clip).ToArray();
			else animations = new AnimationClip[0];

			foreach (var item in bufferTask.Result) {
				item.Dispose();
			}

			onFinished?.Invoke( nodeTask.Result.GetRoot(), animations );
		}
#endregion

#region Async
		private static IEnumerator LoadAsync(string json, string filepath, byte[] bytefile, long binChunkStart, ImportSettings importSettings, Action<GameObject, AnimationClip[]> onFinished, Action<float> onProgress = null) {
			// Threaded deserialization
			Task<GLTFObject> deserializeTask = new Task<GLTFObject>(() => JsonConvert.DeserializeObject<GLTFObject>(json));
			deserializeTask.Start();
			while (!deserializeTask.IsCompleted) yield return null;
			GLTFObject gltfObject = deserializeTask.Result;
			CheckExtensions(gltfObject);

			// directory root is sometimes used for loading buffers from containing file, or local images
			string directoryRoot = filepath != null ? Directory.GetParent(filepath).ToString() + "/" : null;

			importSettings.shaderOverrides.CacheDefaultShaders();

			// Setup import tasks
			List<ImportTask> importTasks = new List<ImportTask>();

			GLTFBuffer.ImportTask bufferTask = new GLTFBuffer.ImportTask(gltfObject.buffers, filepath, bytefile, binChunkStart);
			importTasks.Add(bufferTask);
			GLTFBufferView.ImportTask bufferViewTask = new GLTFBufferView.ImportTask(gltfObject.bufferViews, bufferTask);
			importTasks.Add(bufferViewTask);
			GLTFAccessor.ImportTask accessorTask = new GLTFAccessor.ImportTask(gltfObject.accessors, bufferViewTask);
			importTasks.Add(accessorTask);
			GLTFImage.ImportTask imageTask = new GLTFImage.ImportTask(gltfObject.images, directoryRoot, bufferViewTask);
			importTasks.Add(imageTask);
			GLTFTexture.ImportTask textureTask = new GLTFTexture.ImportTask(gltfObject.textures, imageTask);
			importTasks.Add(textureTask);
			GLTFMaterial.ImportTask materialTask = new GLTFMaterial.ImportTask(gltfObject.materials, textureTask, importSettings);
			importTasks.Add(materialTask);
			GLTFMesh.ImportTask meshTask = new GLTFMesh.ImportTask(gltfObject.meshes, accessorTask, bufferViewTask, materialTask, importSettings);
			importTasks.Add(meshTask);
			GLTFSkin.ImportTask skinTask = new GLTFSkin.ImportTask(gltfObject.skins, accessorTask);
			importTasks.Add(skinTask);
			GLTFNode.ImportTask nodeTask = new GLTFNode.ImportTask(gltfObject.nodes, meshTask, skinTask, gltfObject.cameras);
			importTasks.Add(nodeTask);

			// Ignite
			for (int i = 0; i < importTasks.Count; i++) {
				yield return StaticCoroutine.Start( TaskSupervisor(importTasks[i], onProgress) );
			}

			// Wait for all tasks to finish
			while (!importTasks.All(x => x.IsCompleted)) yield return null;

			// Fire onFinished when all tasks have completed
			GameObject root = nodeTask.Result.GetRoot();
			GLTFAnimation.ImportResult[] animationResult = gltfObject.animations.Import(accessorTask.Result, nodeTask.Result, importSettings);
			AnimationClip[] animations = new AnimationClip[0];
			if (animationResult != null) animations = animationResult.Select(x => x.clip).ToArray();
			if (onFinished != null) onFinished(nodeTask.Result.GetRoot(), animations);

			// Close file streams
			foreach (var item in bufferTask.Result) {
				item.Dispose();
			}
		}

		/// <summary> Keeps track of which threads to start when </summary>
		private static IEnumerator TaskSupervisor(ImportTask importTask, Action<float> onProgress = null) {
			// Wait for required results to complete before starting
			while (!importTask.IsReady) yield return null;
			// Start threaded task
			importTask.task.Start();
			// Wait for task to complete
			while (!importTask.task.IsCompleted) yield return null;
			// Run additional unity code on main thread
			yield return StaticCoroutine.Start( importTask.OnCoroutine(onProgress) );
			//Wait for additional coroutines to complete
			while (!importTask.IsCompleted) { yield return null; }
		}
#endregion

		private static void CheckExtensions(GLTFObject gLTFObject) {
			if (gLTFObject.extensionsRequired != null) {
				for (int i = 0; i < gLTFObject.extensionsRequired.Count; i++) {
					switch (gLTFObject.extensionsRequired[i]) {
						case "KHR_materials_pbrSpecularGlossiness":
							break;
						case "KHR_draco_mesh_compression":
							break;
						case "KHR_texture_basisu":
							break;
						default:
							Debug.LogWarning($"GLTFUtility: Required extension '{gLTFObject.extensionsRequired[i]}' not supported. Import process will proceed but results may vary.");
							break;
					}
				}
			}
		}
	}
}