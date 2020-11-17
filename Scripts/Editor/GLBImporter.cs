using UnityEditor.Experimental.AssetImporters;
using UnityEngine;

namespace Siccity.GLTFUtility {
	[ScriptedImporter(1, "glb")]
	public class GLBImporter : GLTFImporter {

		public override void OnImportAsset(AssetImportContext ctx)
		{
			this.ctx = ctx;

			// Load asset
			if (importSettings == null) importSettings = new ImportSettings();
			Importer.LoadFromFile(ctx.assetPath, importSettings, onFinished, Format.GLB);
		}

		private void onFinished( GameObject root, AnimationClip[ ] animations )
		{
			// Save asset
			GLTFAssetUtility.SaveToAsset( root, animations, ctx );
		}
	}
}