using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Scripting;
using Unity.Collections;

#if KTX
using KtxUnity;
#endif

namespace Siccity.GLTFUtility {
	// https://github.com/KhronosGroup/glTF/blob/master/specification/2.0/README.md#image
	[Preserve] public class GLTFImage {
		/// <summary>
		/// The uri of the image.
		/// Relative paths are relative to the .gltf file.
		/// Instead of referencing an external file, the uri can also be a data-uri.
		/// The image format must be jpg or png.
		/// </summary>
		public string uri;
		/// <summary> Either "image/jpeg" or "image/png" </summary>
		public string mimeType;
		public int? bufferView;
		public string name;

		public class TextureOrientation
		{
			public bool IsXFlipped = false;
			public bool IsYFlipped = false;
		}

		public class ImportResult {
			public byte[] bytes;
			public string path;
			public string mimeType;

			public ImportResult(byte[] bytes, string mimeType, string path = null) {
				this.bytes = bytes;
				this.path = path;
				this.mimeType = mimeType;
			}

			public IEnumerator CreateTextureAsync(bool linear, Action<Texture2D, TextureOrientation> onFinish, string mimeType, Action<float> onProgress = null)
			{
				Texture2D tex = new Texture2D( 2, 2, TextureFormat.ARGB32, true, linear );
				bool loaded = false;
				TextureOrientation orientation = new TextureOrientation();

				//With GLTF, the mimeType stores the path, let's correct that mistake
				if( !string.IsNullOrEmpty( mimeType ) )
				{
					if( File.Exists( mimeType ) )
					{
						path = mimeType;
						mimeType = "image/" + Path.GetExtension( path ).Remove( 0, 1 );
					}
				}

				//Use KtxUnity plugin to load ktx/ktx2/basis textures
				if( mimeType == "image/ktx" || mimeType == "image/ktx2" || mimeType == "image/basis" )
				{
#if !KTX
					Debug.LogError( "GLTFImage.cs CreateTextureAsync() KTX and basis texture support is not enabled, try enabling 'KTX' scripting define symbol in project settings and make sure KtxUnity plugin is in your project" );
					yield break;
#else
					NativeArray<byte> data = new NativeArray<byte>( bytes, KtxNativeInstance.defaultAllocator );
					
					TextureBase textureBase = null;
					
					if( mimeType == "image/ktx" || mimeType == "image/ktx2" )
						textureBase = new KtxTexture();
					else if( mimeType == "image/basis" )
						textureBase = new BasisUniversalTexture();

					textureBase.onTextureLoaded += ( Texture2D texture, KtxUnity.TextureOrientation ktxOrientation ) =>
					{
						orientation.IsXFlipped = ktxOrientation.IsXFlipped();
						orientation.IsYFlipped = ktxOrientation.IsYFlipped();
						tex = texture;

						//Rename the texture if we have a valid path variable (not available with .glb)
						if( !string.IsNullOrEmpty( path ) )
							tex.name = Path.GetFileNameWithoutExtension( path );
						//
						if( tex.name == "material1_normal" )
							Debug.Log( "OnTextureLoaded() " + tex.name + "[ " + tex.width + " x " + tex.height + " ] Flipped[ " + orientation.IsXFlipped + " : " + orientation.IsYFlipped + " ]" );
						//
						loaded = true;
					};
					
					yield return StaticCoroutine.Start( textureBase.LoadBytesRoutine( data, true ) );

					data.Dispose();
#endif
				}
				else //Load .jpg, .jpeg, .png textures
				{
					orientation.IsXFlipped = false;
					orientation.IsYFlipped = false;
					tex.LoadImage( bytes );
					loaded = true;
				}

				yield return new WaitUntil( () =>
				{
					/*
					if( tex.name == "material1_basecolor" )
						Debug.Log( "WaitUntil() " + tex.name + "[ loaded = " + loaded + " ][ " + tex.width + " x " + tex.height + " ]" );
					*/
					return loaded;
				} );
				
				if( tex != null )
				{
					//Rename the texture if we have a valid path variable (not available with .glb)
					if( !string.IsNullOrEmpty( path ) )
						tex.name = Path.GetFileNameWithoutExtension( path );

					/*
					if( tex.name == "material1_basecolor" )
						Debug.Log( "OnFinish() Transcoded " + tex.name + "[" + mimeType + "][ " + bytes.Length + " ][ " + tex.width + " x " + tex.height + " ]" );
					*/
					onFinish( tex, orientation );
				}
				else
				{
					Debug.Log( "OnFinish() Unable To Transcode " + tex.name + "[" + mimeType + "][ " + bytes.Length + " ]" );
				}

			} //END CreateTextureAsync()

		} //END ImportResult class

		public class ImportTask : Importer.ImportTask<ImportResult[]> {
			public ImportTask(List<GLTFImage> images, string directoryRoot, GLTFBufferView.ImportTask bufferViewTask) : base(bufferViewTask) {
				task = new Task(() => {
					// No images
					if (images == null) return;

					Result = new ImportResult[images.Count];
					for (int i = 0; i < images.Count; i++) {
						string fullUri = directoryRoot + images[i].uri;
						if (!string.IsNullOrEmpty(images[i].uri)) {
							if (File.Exists(fullUri)) {
								// If the file is found at fullUri, read it
								byte[] bytes = File.ReadAllBytes(fullUri);
								Result[i] = new ImportResult(bytes, fullUri, images[i].mimeType);
							} else if (images[i].uri.StartsWith("data:")) {
								// If the image is embedded, find its Base64 content and save as byte array
								string content = images[i].uri.Split(',').Last();
								byte[] imageBytes = Convert.FromBase64String(content);
								Result[i] = new ImportResult(imageBytes, images[i].mimeType);
							}
						} else if (images[i].bufferView.HasValue && !string.IsNullOrEmpty(images[i].mimeType)) {
							GLTFBufferView.ImportResult view = bufferViewTask.Result[images[i].bufferView.Value];
							byte[] bytes = new byte[view.byteLength];
							view.stream.Position = view.byteOffset;
							view.stream.Read(bytes, 0, view.byteLength);
							Result[i] = new ImportResult(bytes, images[i].mimeType);
						} else {
							Debug.Log("Couldn't find texture at " + fullUri);
						}
					}
				});
			}
		}
	}
}