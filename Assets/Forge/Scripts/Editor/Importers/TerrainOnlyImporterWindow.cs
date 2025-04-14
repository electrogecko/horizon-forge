using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.IO;
using System.Collections.Generic;

// DL NOT IMPLEMENTED 
public class TerrainOnlyImporterWindow : EditorWindow
{
	const string WindowTitle = "Terrain-Only Importer";

	bool readOcclusion = true;
	string occlusionFolderOverride = "";
    string terrainBinFolder = "";
    int levelId = 0;
    int racVersion = 3; // default to UYA
    string mapName = "";

	void OnEnable()
	{
		if (string.IsNullOrEmpty(mapName))
			mapName = SceneManager.GetActiveScene().name;
	}


    static readonly List<string> RacVersions = new List<string> { "DL", "UYA", "GC" };
    static readonly List<int> RacVersionInts = new List<int> { 4, 3, 2 };

	
    [MenuItem("Forge/Tools/Importers/Terrain-Only Importer")]
    public static void ShowWindow()
    {
        var wnd = GetWindow<TerrainOnlyImporterWindow>();
        wnd.titleContent = new GUIContent("Terrain-Only Importer");
    }

    void OnGUI()
    {
		
		
        GUILayout.Space(10);
        EditorGUILayout.LabelField("Import Terrain From terrain.bin", EditorStyles.boldLabel);
        GUILayout.Space(10);

        // Select terrain.bin folder
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("terrain.bin Folder", GUILayout.Width(120));
        terrainBinFolder = EditorGUILayout.TextField(terrainBinFolder);
        if (GUILayout.Button("Browse", GUILayout.Width(80)))
        {
            terrainBinFolder = EditorUtility.OpenFolderPanel("Select terrain.bin folder", "", "");
        }
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(10);

        // Map name
        mapName = EditorGUILayout.TextField("Map Name", mapName);

        // Level ID
        levelId = EditorGUILayout.IntField("Level ID", levelId);

        // RAC version dropdown
        racVersion = RacVersionInts[EditorGUILayout.Popup("RAC Version", RacVersionInts.IndexOf(racVersion), RacVersions.ToArray())];

        GUILayout.Space(20);

        if (GUILayout.Button("Import Terrain"))
		{
			GameObject rootGo = ImportPreparedTfragsFromBin(terrainBinFolder, levelId, racVersion, mapName);

			if (readOcclusion && rootGo != null)
			{
				var tfrag = rootGo.GetComponentInChildren<Tfrag>();
				if (tfrag)
					ReadTfragChunksWithCustomOcclusion(terrainBinFolder, occlusionFolderOverride, tfrag);
			}
		}

		
		// Add occlusion toggle and override path
		readOcclusion = EditorGUILayout.Toggle("Read Occlusion", readOcclusion);

		if (readOcclusion)
		{
			string label = "Occlusion Folder";
			string browseButton = "Browse";
			string fallbackPath = "";

			if (!string.IsNullOrEmpty(terrainBinFolder))
			{
				fallbackPath = Path.GetFullPath(Path.Combine(terrainBinFolder, "../../gameplay/occlusion"));
			}

			if (string.IsNullOrEmpty(occlusionFolderOverride) && !string.IsNullOrEmpty(fallbackPath))
			{
				occlusionFolderOverride = fallbackPath;
			}

			// Wrap layout in a true Begin/End block
			EditorGUILayout.BeginHorizontal();
			{
				EditorGUILayout.LabelField(label, GUILayout.Width(120));
				occlusionFolderOverride = EditorGUILayout.TextField(occlusionFolderOverride);
				if (GUILayout.Button(browseButton, GUILayout.Width(80)))
				{
					string chosen = EditorUtility.OpenFolderPanel("Select Occlusion Folder", "", "");
					if (!string.IsNullOrEmpty(chosen))
						occlusionFolderOverride = chosen;
				}
			}
			EditorGUILayout.EndHorizontal();
		}



	}

    GameObject ImportPreparedTfragsFromBin(string binFolder, int levelId, int racVersion, string mapName)
	{
		string inputBinFolder = binFolder;
		string outputMapFolder = FolderNames.GetMapFolder(mapName);
		var assetOutputFolder = Path.Combine("Assets", "Maps", mapName);

		string terrainBinFile = Path.Combine(binFolder, "terrain.bin");
		string terrainDaeFile = Path.Combine(binFolder, "terrain.dae");

		Debug.Log($"[TFRAG] Selected terrain bin folder: {binFolder}");
		Debug.Log($"[TFRAG] Expected terrain.bin: {terrainBinFile}");
		Debug.Log($"[TFRAG] Expected terrain.dae: {terrainDaeFile}");

		if (!File.Exists(terrainBinFile))
		{
			Debug.LogError("terrain.bin not found.");
			return null;
		}

		var texFiles = Directory.GetFiles(binFolder, "*.0.png");
		bool daeMissing = !File.Exists(terrainDaeFile);
		bool missingTextures = texFiles.Length == 0;

		if (daeMissing || missingTextures)
		{
			if (daeMissing)
				Debug.Log("[TFRAG] terrain.dae is missing.");
			if (missingTextures)
				Debug.Log("[TFRAG] No .0.png texture files found — re-exporting.");
			
			Debug.Log($"[TFRAG] Exporting tfrags with racVersion={racVersion}");
			Debug.Log($"[TFRAG] Export input: {terrainBinFile}");
			Debug.Log($"[TFRAG] Export output: {terrainDaeFile}");

			WrenchHelper.ExportTfrags(terrainBinFile, terrainDaeFile, racVersion);
		}
		else
		{
			Debug.Log($"[TFRAG] Found existing terrain.dae and {texFiles.Length} textures — skipping export.");
		}
		
		// Confirm again after export
		texFiles = Directory.GetFiles(binFolder, "*.0.png");
		Debug.Log($"[TFRAG] After export: Found {texFiles.Length} texture files.");

		if (!File.Exists(terrainDaeFile))
		{
			Debug.LogError("Failed to generate terrain.dae");
			return null;
		}

		var rootGo = GameObject.FindObjectOfType<MapConfig>()?.gameObject;
		Selection.activeGameObject = rootGo;
		EditorGUIUtility.PingObject(rootGo);

		if (!rootGo)
		{
			rootGo = new GameObject(mapName);
		}

		var assetImports = new List<PackerImporterWindow.PackerAssetImport>();
		var mapFolder = FolderNames.GetMapFolder(mapName);
		var binOutputFolder = FolderNames.GetMapBinFolder(mapName, racVersion);

		ImportTfrags(inputBinFolder, assetOutputFolder, rootGo, racVersion, levelId);

		PackerImporterWindow.Import(assetImports, true);

		Debug.Log("Terrain import complete.");

		return rootGo;
	}


	void ReadTfragChunks(string mapBinFolder, Tfrag tfrag)
	{
		string terrainBinFile = Path.Combine(mapBinFolder, "terrain.bin");
		if (!File.Exists(terrainBinFile))
		{
			Debug.LogWarning("[TFRAG] Missing terrain.bin for initial TfragChunk setup.");
			return;
		}

		Debug.Log("[TFRAG] ReadTfragChunks() was called");

		using (var fs = File.OpenRead(terrainBinFile))
		using (var reader = new BinaryReader(fs))
		{
			var packetStart = reader.ReadInt32();
			var packetCount = reader.ReadInt32();

			var chunkOffsets = new List<int>(packetCount);

			// Read all offsets first
			for (int i = 0; i < packetCount; i++)
			{
				fs.Position = packetStart + (i * 0x40) + 0x10;
				int dataOffset = reader.ReadInt32();
				chunkOffsets.Add(dataOffset);
			}

			for (int i = 0; i < packetCount; i++)
			{
				var chunkTransform = tfrag.transform.Find($"tfrag_{i}");
				if (!chunkTransform)
				{
					Debug.LogWarning($"[TFRAG] Missing tfrag chunk {i}");
					continue;
				}

				var chunk = chunkTransform.gameObject.AddComponent<TfragChunk>();
				Debug.Log($"[TFRAG] Adding chunk {i}");

				// Read header
				fs.Position = packetStart + (i * 0x40);
				chunk.HeaderBytes = reader.ReadBytes(0x40);
				chunk.OcclusionId = BitConverter.ToInt16(chunk.HeaderBytes, 0x3a);

				// Data range
				int dataOffset = chunkOffsets[i];
				int dataLen = (i + 1 < packetCount)
					? chunkOffsets[i + 1] - dataOffset
					: (int)(fs.Length - (packetStart + dataOffset));

				fs.Position = packetStart + dataOffset;
				chunk.DataBytes = reader.ReadBytes(dataLen);

				Debug.Log($"[TFRAG] Chunk {i} data offset: {dataOffset}, length: {dataLen}");
			}
		}
	}




    void ReadTfragChunksWithCustomOcclusion(string mapBinFolder, string occlusionFolder, Tfrag tfrag)
	{
		var terrainBinFile = Path.Combine(mapBinFolder, "terrain.bin");
		var tfragOcclusionFile = Path.Combine(occlusionFolder, "tfrag.bin");

		if (!File.Exists(terrainBinFile)) return;

		Debug.Log("[TFRAG] ReadTfragChunksWithCustomOcclusion() was called");

		var instancesById = new Dictionary<int, TfragChunk>();

		using (var fs = File.OpenRead(terrainBinFile))
		using (var reader = new BinaryReader(fs))
		{
			var packetStart = reader.ReadInt32();
			var packetCount = reader.ReadInt32();

			var chunkOffsets = new List<int>(packetCount);

			// Pre-pass: read all offsets
			for (int i = 0; i < packetCount; i++)
			{
				fs.Position = packetStart + (i * 0x40) + 0x10;
				int dataOffset = reader.ReadInt32();
				chunkOffsets.Add(dataOffset);
			}

			for (int i = 0; i < packetCount; i++)
			{
				var chunkTransform = tfrag.transform.Find($"tfrag_{i}");
				if (!chunkTransform)
				{
					Debug.LogWarning($"[TFRAG] Missing tfrag chunk {i}");
					continue;
				}

				var tfragChunk = chunkTransform.gameObject.AddComponent<TfragChunk>();

				fs.Position = packetStart + (i * 0x40);
				tfragChunk.HeaderBytes = reader.ReadBytes(0x40);
				tfragChunk.OcclusionId = BitConverter.ToInt16(tfragChunk.HeaderBytes, 0x3a);
				instancesById[i] = tfragChunk;

				int dataOffset = chunkOffsets[i];
				int dataLen = (i + 1 < packetCount)
					? chunkOffsets[i + 1] - dataOffset
					: (int)(fs.Length - (packetStart + dataOffset));

				fs.Position = packetStart + dataOffset;
				tfragChunk.DataBytes = reader.ReadBytes(dataLen);
			}
		}

		// Load occlusion octants from tfrag.bin
		if (File.Exists(tfragOcclusionFile))
		{
			var tfragOcclusion = File.ReadAllBytes(tfragOcclusionFile);
			using (var ms = new MemoryStream(tfragOcclusion))
			using (var occlusionReader = new BinaryReader(ms))
			{
				while (occlusionReader.BaseStream.Position < occlusionReader.BaseStream.Length)
				{
					var octants = PackerHelper.ReadOcclusionBlock(occlusionReader, out var instanceIdx, out var occlusionId).ToArray();
					if (instancesById.TryGetValue(instanceIdx, out var chunk))
					{
						chunk.Octants = octants;
					}
					else
					{
						Debug.LogWarning($"No tfrag match for occlusion instance {instanceIdx}");
					}
				}
			}
		}
	}



	void ImportTfrags(string mapBinFolder, string mapResourcesFolder, GameObject rootGo, int racVersion, int levelId)
	{
//		string terrainBinFile = Path.Combine(mapBinFolder, "terrain.bin");
		string terrainBinFolder = mapBinFolder;
		string terrainOutColladaFile = Path.Combine(mapBinFolder, "terrain.dae");


		string terrainFolderName = $"rc{racVersion}_level{levelId}";
		//string terrainMapResourcesFolder = Path.Combine(mapResourcesFolder, "Tfrags", terrainFolderName);
		//string terrainTexturesMapResourcesFolder = Path.Combine(terrainMapResourcesFolder, "Textures");
		//string terrainMaterialsMapResourcesFolder = Path.Combine(terrainMapResourcesFolder, "Materials");

		string terrainBinFile = Path.Combine(mapBinFolder, "terrain.bin");
		string terrainDaeFile = Path.Combine(mapBinFolder, "terrain.dae");
		var terrainMapResourcesFolder = Path.Combine(mapResourcesFolder, "Tfrag", $"rc{racVersion}_level{levelId}");
		string terrainTexturesMapResourcesFolder = Path.Combine(terrainMapResourcesFolder, "Textures");
		string terrainMaterialsMapResourcesFolder = Path.Combine(terrainMapResourcesFolder, "Materials");

		var shader = Shader.Find("Horizon Forge/Universal");

		if (!File.Exists(terrainBinFile)) {
			Debug.LogError("Missing terrain.bin file.");
			return;
		}

		// Prepare folders
		if (Directory.Exists(terrainMapResourcesFolder)) Directory.Delete(terrainMapResourcesFolder, true);
		Directory.CreateDirectory(terrainMapResourcesFolder);
		Directory.CreateDirectory(terrainTexturesMapResourcesFolder);
		Directory.CreateDirectory(terrainMaterialsMapResourcesFolder);

		//// Export if needed (redundant here, but included for parity)
		//if (!File.Exists(terrainOutColladaFile)) {
		//	Debug.LogWarning("terrain.dae not found. You must run ExportTfrags before calling this.");
		//	return;
		//}
		// If no textures exist yet, attempt to extract them from ISO via fallback unpack
		if (Directory.GetFiles(terrainBinFolder, "*.0.png").Length == 0)
		{
			Debug.Log("[TFRAG] No .0.png textures found next to terrain.bin. Attempting fallback extraction via ISO...");

			var settings = ForgeSettings.Load();
			string isoPath = racVersion switch
			{
				4 => settings.PathToCleanDeadlockedIso,
				3 => settings.GetPathToCleanUyaIso(),
				2 => settings.PathToCleanGcIso,
				_ => throw new NotImplementedException()
			};

			string tempWadOut = Path.Combine("Temp", "Wads", $"rc{racVersion}_level{levelId}.wad");
			ExtractWadFromISO_TOIW(isoPath, levelId, racVersion, tempWadOut);

			string unpackWorkingDir = Path.GetDirectoryName(tempWadOut); // Same as wad path parent
			var decompressResult = PackerHelper.DecompressAndUnpackLevelWad(tempWadOut, unpackWorkingDir);
			if (decompressResult != PackerHelper.PACKER_STATUS_CODES.SUCCESS)
			{
				Debug.LogWarning($"[TFRAG] Failed to decompress and unpack level wad: {decompressResult}");
				return;
			}

			string unpackAssetsDir = Path.Combine(unpackWorkingDir, "assets");
			var assetsResult = PackerHelper.UnpackAssets(unpackWorkingDir, unpackAssetsDir, racVersion);


			string fallbackTexDir = Path.Combine(unpackAssetsDir, "terrain");
			if (Directory.Exists(fallbackTexDir))
			{
				// foreach (var fallbackTex in Directory.GetFiles(fallbackTexDir, "*.0.png"))
				// {
					// var fileName = Path.GetFileName(fallbackTex);
					// var dest = Path.Combine(terrainTexturesMapResourcesFolder, fileName);
					// File.Copy(fallbackTex, dest, true);
					// Debug.Log($"[TFRAG] Fallback copied: {fileName}");
				// }
				foreach (var fallbackTex in Directory.GetFiles(fallbackTexDir, "*.0.png"))
				{
					var fileName = Path.GetFileName(fallbackTex);             // tex.0080.0.png
					var texIdx = int.Parse(fileName.Split('.')[1]);           // 0080 -> 80
					var newName = $"tfrags-{texIdx}.png";
					var dest = Path.Combine(terrainTexturesMapResourcesFolder, newName);
					File.Copy(fallbackTex, dest, true);
					Debug.Log($"[TFRAG] Fallback copied: {newName}");
				}

				AssetDatabase.Refresh();
			}
			else
			{
				Debug.LogWarning($"[TFRAG] Fallback texture directory not found: {fallbackTexDir}");
			}
		}

		var wrappings = WrenchHelper.GetColladaTextureWraps(terrainOutColladaFile);

		var texFiles = Directory.GetFiles(terrainTexturesMapResourcesFolder, "tfrags-*.png");
		foreach (var texFile in texFiles)
		{
			var fileName = Path.GetFileNameWithoutExtension(texFile); // e.g. "tfrags-80"
			if (!fileName.StartsWith("tfrags-")) continue;

			if (!int.TryParse(fileName.Substring("tfrags-".Length), out int texIdx))
			{
				Debug.LogWarning($"[TFRAG] Could not parse texture index from {fileName}");
				continue;
			}

			var wrap = wrappings?.GetValueOrDefault(texIdx);

			// Import path
			var unityTexPath = UnityHelper.GetProjectRelativePath(texFile);

			// Material path
			var outMatFile = Path.Combine(terrainMaterialsMapResourcesFolder, $"tfrags-{texIdx}.mat");
			var unityMatPath = UnityHelper.GetProjectRelativePath(outMatFile);

			var mat = AssetDatabase.LoadAssetAtPath<Material>(unityMatPath);
			bool matAlreadyExists = mat;

			if (!mat)
				mat = new Material(shader);

			var texAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(unityTexPath);
			mat.SetTexture("_MainTex", texAsset);
			EditorUtility.SetDirty(mat);

			if (matAlreadyExists)
			{
				AssetDatabase.SaveAssetIfDirty(mat);
			}
			else
			{
				var matDir = Path.GetDirectoryName(unityMatPath);
				if (!Directory.Exists(matDir))
					Directory.CreateDirectory(matDir);

				AssetDatabase.CreateAsset(mat, unityMatPath);
			}

		}

		// 1. Import mesh
		BlenderHelper.ImportMesh(terrainOutColladaFile, terrainMapResourcesFolder, "tfrags", overwrite: true, out var outMeshFile, fixNormals: false);

		// 2. Set import settings BEFORE instantiating or deleting
		string meshAssetPath = UnityHelper.GetProjectRelativePath(outMeshFile);
		AssetDatabase.ImportAsset(meshAssetPath);
		SetModelImportSettings(meshAssetPath, addCollider: false, remapMaterials: true);

		// 3. Load and instantiate prefab
		var tfragPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(meshAssetPath); 
		var tfragGo = (GameObject)GameObject.Instantiate(tfragPrefab);
		if (tfragGo)
		{
			tfragGo.name = "tfrags";
			tfragGo.transform.SetParent(rootGo.transform, true);
			tfragGo.transform.localPosition = Vector3.zero;
			tfragGo.transform.localRotation = Quaternion.identity;
			tfragGo.transform.localScale = Vector3.one;

			var tfrag = tfragGo.AddComponent<Tfrag>();
			tfragGo.layer = LayerMask.NameToLayer("TFRAG");
			UnityHelper.RecurseHierarchy(tfragGo.transform, (t) => t.gameObject.layer = tfragGo.layer);

			for (int i = 0; i < tfragGo.transform.childCount; ++i)
			{
				var child = tfragGo.transform.GetChild(i);
				var mf = child.GetComponent<MeshFilter>();
				if (mf)
					mf.sharedMesh = mf.sharedMesh.Clone();
			}

			// load per-chunk metadata before deleting prefab
			ReadTfragChunks(terrainBinFolder, tfrag);
		}


		
		// Force Unity to re-import the model with materials now in place
		AssetDatabase.ImportAsset(UnityHelper.GetProjectRelativePath(outMeshFile), ImportAssetOptions.ForceUpdate);
	}

	void SetModelImportSettings(string modelAssetPath, bool addCollider = false, bool remapMaterials = true)
	{
		var importer = AssetImporter.GetAtPath(modelAssetPath) as ModelImporter;
		if (importer != null)
		{
			importer.isReadable = true;
			importer.addCollider = addCollider;

			importer.preserveHierarchy = true;
			importer.bakeAxisConversion = true;
			importer.importNormals = ModelImporterNormals.Calculate;

			if (remapMaterials)
			{
				importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
				importer.SearchAndRemapMaterials(
					ModelImporterMaterialName.BasedOnModelNameAndMaterialName,
					ModelImporterMaterialSearch.Local);
			}
			else
			{
				importer.materialImportMode = ModelImporterMaterialImportMode.None;
			}

			EditorUtility.SetDirty(importer);
			importer.SaveAndReimport();
		}
	}

	void ExtractWadFromISO_TOIW(string isoPath, int levelId, int racVersion, string outWadFilePath)
		{
			var outDir = FolderNames.GetTempFolder();
			var result = PackerHelper.ExtractLevelWads(isoPath, outDir, levelId, racVersion);
			var dstDir = Path.GetDirectoryName(outWadFilePath);
			Directory.CreateDirectory(dstDir);  // Ensure path exists


			if (racVersion == RCVER.DL) // DL IS NOT IMPLEMENTED 
			{
				var addFilesToCopy = new[]
				{
					"chunk0.wad",
					"chunk1.wad",
					"sound.bnk"
				};

				var expectedWad = Path.Combine(outDir, $"core_level.wad");
				if (result != PackerHelper.PACKER_STATUS_CODES.SUCCESS || !File.Exists(expectedWad))
				{
					EditorUtility.DisplayDialog(WindowTitle, $"Failed to extract wad from ISO: {result}.", "Ok");
					return;
				}

				File.Copy(expectedWad, outWadFilePath, true);
				foreach (var file in addFilesToCopy)
				{
					var srcFile = Path.Combine(outDir, file);
					if (File.Exists(srcFile))
						File.Copy(srcFile, Path.Combine(dstDir, file), true);
				}
			}
			else
			{
				var addFilesToCopy = new Dictionary<string, string>()
				{
					{ $"level{levelId}.1.wad", $"sound.bnk" },
					{ $"level{levelId}.2.wad", $"level{levelId}.2.wad" }, // gameplay
				};

				var expectedWad = Path.Combine(outDir, $"level{levelId}.0.wad");
				if (result != PackerHelper.PACKER_STATUS_CODES.SUCCESS || !File.Exists(expectedWad))
				{
					EditorUtility.DisplayDialog(WindowTitle, $"Failed to extract wad from ISO: {result}.", "Ok");
					return;
				}

				File.Copy(expectedWad, outWadFilePath, true);
				foreach (var file in addFilesToCopy)
				{
					var srcFile = Path.Combine(outDir, file.Key);
					if (File.Exists(srcFile))
						File.Copy(srcFile, Path.Combine(dstDir, file.Value), true);
				}
			}
		}





}
