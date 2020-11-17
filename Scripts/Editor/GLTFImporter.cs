using UnityEditor.Experimental.AssetImporters;
using UnityEngine;

namespace Siccity.GLTFUtility {
	[ScriptedImporter(1, "gltf")]
	public class GLTFImporter : ScriptedImporter {

		public AssetImportContext ctx;
		public ImportSettings importSettings;

		public override void OnImportAsset(AssetImportContext ctx)
		{
			this.ctx = ctx;

			// Load asset
			if (importSettings == null) importSettings = new ImportSettings();
			Importer.LoadFromFile(ctx.assetPath, importSettings, onFinished, Format.GLTF);
		}

		private void onFinished( GameObject root, AnimationClip[ ] animations )
		{
			// Save asset
			GLTFAssetUtility.SaveToAsset( root, animations, ctx );
		}
	}
}