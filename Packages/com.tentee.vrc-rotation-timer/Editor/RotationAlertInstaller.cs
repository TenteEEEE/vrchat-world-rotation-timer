#if UNITY_EDITOR
using System.IO;
using System.Text;
using RotationAlert;
using TMPro;
using UdonSharp;
using UdonSharp.Compiler;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.Udon;

namespace RotationAlertEditor
{
    // Primary partial: paths, design tokens, label strings, menu items, program-asset / font
    // generation, and the two prefab builders. RotationAlertInstaller.Ui.cs holds the UI
    // factory helpers used below. Self-contained: no references to PocketConveni/PocketOverdrive.
    public static partial class RotationAlertInstaller
    {
        // ------------------------------------------------------------------
        // Paths (SPEC §7)
        // ------------------------------------------------------------------
        private const string PackageRootDir = "Packages/com.tentee.vrc-rotation-timer/";
        private const string LegacyRootDir = "Assets/RotationAlert/";

        // The package is normally imported under Packages/. The Assets/ path keeps the
        // same installer usable from the upstream Unity project where the package is authored.
        private static string RootDir
        {
            get
            {
                return AssetDatabase.IsValidFolder(PackageRootDir.TrimEnd('/'))
                    ? PackageRootDir
                    : LegacyRootDir;
            }
        }

        private static string RuntimeDir { get { return RootDir + "Runtime/"; } }
        private static string CorePath { get { return RuntimeDir + "RotationAlertCore"; } }
        private static string DisplayPath { get { return RuntimeDir + "RotationAlertDisplay"; } }
        private static string AudioPath { get { return RuntimeDir + "RotationAlertAudio"; } }
        private static string BridgePath { get { return RuntimeDir + "RotationAlertShaderBridge"; } }

        private static string FontDir { get { return RootDir + "Fonts/"; } }
        private static string FontSourcePath { get { return FontDir + "NotoSansJP-Regular.otf"; } }
        private static string FontAssetPath { get { return FontDir + "RotationAlert JP SDF.asset"; } }

        private static string AudioDir { get { return RootDir + "Audio/"; } }
        private static string GeneratedDir { get { return RootDir + "Generated/"; } }
        private static string FillSpritePath { get { return GeneratedDir + "rotation_alert_fill.png"; } }
        private static string UiMaterialPath { get { return GeneratedDir + "RotationAlert UI Material.mat"; } }
        private static string TmpMaterialPath { get { return GeneratedDir + "RotationAlert TMP Material.mat"; } }

        private static string PrefabDir { get { return RootDir + "Prefabs/"; } }
        private static string PanelPrefabPath { get { return PrefabDir + "RotationAlert Panel.prefab"; } }
        private static string DisplayPrefabPath { get { return PrefabDir + "RotationAlert Display.prefab"; } }

        private const string ExportPackagePath = "RotationAlert.unitypackage";

        private const string PanelBuildRootName = "RotationAlert Panel (Build Temp)";
        private const string DisplayBuildRootName = "RotationAlert Display (Build Temp)";

        // ------------------------------------------------------------------
        // Design tokens (SPEC §6)
        // ------------------------------------------------------------------
        private static readonly Color TokenBackground = new Color32(0x07, 0x13, 0x1B, 0xFF);
        private static readonly Color TokenSurface = new Color32(0x0F, 0x24, 0x31, 0xFF);
        private static readonly Color TokenText = new Color32(0xEA, 0xF4, 0xF5, 0xFF);
        private static readonly Color TokenMuted = new Color32(0x8F, 0xA8, 0xB3, 0xFF);
        private static readonly Color TokenPrimary = new Color32(0x1D, 0x8A, 0x93, 0xFF);
        private static readonly Color TokenDanger = new Color32(0xE0, 0x5A, 0x4E, 0xFF);
        // Idle phase color (SPEC §4) — used as the installer's initial accent/fill tint before
        // RotationAlertDisplay starts repainting it every frame.
        private static readonly Color TokenIdle = new Color32(0x6B, 0x7A, 0x85, 0xFF);

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        // ------------------------------------------------------------------
        // Label strings — kept as constants in one place so AllUiCharacters (below) is
        // trivially a concatenation of every string the installer or the runtime displays.
        // Runtime-only strings (composed by RotationAlertDisplay at play time, never emitted
        // literally by the installer) are included too — see SPEC §4 for their composition.
        // ------------------------------------------------------------------
        private const string LabelStart = "開始";
        private const string LabelPause = "一時停止";
        private const string LabelResume = "再開";
        private const string LabelSkip = "次へ";
        private const string LabelSkipArmed = "本当に？";
        private const string LabelPlusMinute = "+1分";
        private const string LabelMinusMinute = "-1分";
        private const string LabelReset = "リセット";
        private const string LabelResetArmed = "本当に初期化";
        private const string LabelMuteOn = "ローカル音: ON";
        private const string LabelMuteOff = "ローカル音: OFF";
        private const string LabelSettingsCaption = "設定";
        private const string LabelSettingRotation = "ローテ\n時間";
        private const string LabelSettingInterval = "休憩\n時間";
        private const string LabelSettingCount = "ローテ\n回数";
        private const string LabelSettingWarn = "予告\n時刻";
        private const string LabelMinusGlyph = "-";
        private const string LabelPlusGlyph = "+";

        // Runtime-composed strings (RotationAlertDisplay), not emitted by the installer itself.
        private const string RuntimeIdle = "待機中";
        private const string RuntimeRotationWord = "ローテーション";
        private const string RuntimeIntervalWord = "インターバル（次: ";
        private const string RuntimeIntervalClose = "）";
        private const string RuntimePausedSuffix = "一時停止中";
        private const string RuntimeAllFinished = "全ローテ終了";
        private const string RuntimeUntilEnd = "終了まで";
        private const string RuntimeWarnSuffix = "分前";
        private const string RuntimeUntilNextStart = "次のローテ開始まで";
        private const string RuntimeIdleSummary = "分 × 回 ／ 休憩 分 ／ 予告 分前";
        private const string RuntimeDone = "おつかれさまでした";
        private const string RuntimeSettingsLine = "ローテ 分 ／ 休憩 分 ／ 回数 ／ 予告 分前";

        private static readonly string[] AllLabelStrings =
        {
            LabelStart, LabelPause, LabelResume, LabelSkip, LabelPlusMinute, LabelMinusMinute,
            LabelSkipArmed,
            LabelReset, LabelResetArmed, LabelMuteOn, LabelMuteOff, LabelSettingsCaption,
            LabelSettingRotation, LabelSettingInterval, LabelSettingCount, LabelSettingWarn,
            LabelMinusGlyph, LabelPlusGlyph,
            RuntimeIdle, RuntimeRotationWord, RuntimeIntervalWord, RuntimeIntervalClose,
            RuntimePausedSuffix, RuntimeAllFinished, RuntimeUntilEnd, RuntimeWarnSuffix,
            RuntimeUntilNextStart, RuntimeIdleSummary, RuntimeDone, RuntimeSettingsLine,
        };

        private static string BuildAllUiCharacters()
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < AllLabelStrings.Length; i++) sb.Append(AllLabelStrings[i]);
            // ASCII printable range 0x20..0x7E (digits, mm:ss colon, ON/OFF, punctuation).
            for (int c = 0x20; c <= 0x7E; c++) sb.Append((char)c);
            return sb.ToString();
        }

        private static readonly string AllUiCharacters = BuildAllUiCharacters();

        // ------------------------------------------------------------------
        // Menu items (SPEC §7)
        // ------------------------------------------------------------------
        [MenuItem("Tools/TenteEEEE/Rotation Alert/Build Prefabs")]
        public static void BuildPrefabsMenu()
        {
            EnsureProgramAssets();
            TMP_FontAsset font = EnsureFontAsset();
            EnsureDirectories();
            Material uiMaterial = EnsureUiMaterial(UiMaterialPath, "RotationAlert UI Material");
            Material tmpMaterial = EnsureTmpMaterial(font, TmpMaterialPath, "RotationAlert TMP Material");
            BuildPanelPrefab(font, uiMaterial, tmpMaterial);
            BuildDisplayPrefab(font, uiMaterial, tmpMaterial);
            AssetDatabase.SaveAssets();
            Debug.Log("[RotationAlert] Build Prefabs complete.");
        }

        // batchmode counterpart of Build Prefabs (Unity -executeMethod). Exits with 0 on success,
        // 2 when the program assets were created for the first time (run once more), 1 otherwise.
        public static void BuildPrefabsBatch()
        {
            int exitCode = 0;
            try
            {
                BuildPrefabsMenu();
            }
            catch (System.Exception e)
            {
                exitCode = e.Message.Contains("first time") ? 2 : 1;
                Debug.LogError("[RotationAlert] BuildPrefabsBatch failed: " + e);
            }
            EditorApplication.Exit(exitCode);
        }

        [MenuItem("Tools/TenteEEEE/Rotation Alert/Install Panel into Current Scene")]
        public static void InstallPanelMenu()
        {
            EnsureBuildAssetsAvailable();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPrefabPath);
            if (prefab == null)
            {
                Debug.LogError("[RotationAlert] Missing prefab after build: " + PanelPrefabPath);
                return;
            }
            Scene activeScene = SceneManager.GetActiveScene();
            GameObject existing = FindPanelInstance(prefab, activeScene);
            if (existing != null)
            {
                // Rebuilding the prefab updates its existing instances automatically.
                // Keep the scene instance (and therefore its placement and local overrides)
                // instead of creating a duplicate at the default install position.
                Selection.activeGameObject = existing;
                EditorSceneManager.MarkSceneDirty(activeScene);
                Debug.Log("[RotationAlert] Updated existing Panel instance; kept its scene placement.");
                return;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, activeScene);
            instance.transform.position = new Vector3(0f, 1.3f, 0f);
            Undo.RegisterCreatedObjectUndo(instance, "Install Rotation Alert Panel");
            Selection.activeGameObject = instance;
            EditorSceneManager.MarkSceneDirty(activeScene);
        }

        private static GameObject FindPanelInstance(GameObject prefab, Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded || prefab == null) return null;

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                GameObject root = roots[i];
                if (PrefabUtility.GetCorrespondingObjectFromSource(root) == prefab) return root;
            }
            return null;
        }

        [MenuItem("Tools/TenteEEEE/Rotation Alert/Export UnityPackage")]
        public static void ExportUnityPackageMenu()
        {
            AssetDatabase.ExportPackage(RootDir.TrimEnd('/'), ExportPackagePath, ExportPackageOptions.Recurse);
            Debug.Log("[RotationAlert] Exported " + ExportPackagePath);
        }

        public static void ExportUnityPackageBatch()
        {
            int exitCode = 0;
            try { ExportUnityPackageMenu(); }
            catch (System.Exception e) { exitCode = 1; Debug.LogError("[RotationAlert] Export failed: " + e); }
            EditorApplication.Exit(exitCode);
        }

        private static void EnsureBuildAssetsAvailable()
        {
            // VPM installs already contain the generated program assets, font, sprite, and
            // prefabs. This avoids writing into an immutable PackageCache directory on the
            // first install. The upstream Assets/ copy still rebuilds normally when a source
            // asset is missing.
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PanelPrefabPath) != null &&
                AssetDatabase.LoadAssetAtPath<GameObject>(DisplayPrefabPath) != null &&
                AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(CorePath + ".asset") != null &&
                AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(DisplayPath + ".asset") != null &&
                AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(AudioPath + ".asset") != null &&
                AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(BridgePath + ".asset") != null)
                return;

            BuildPrefabsMenu();
        }

        // ------------------------------------------------------------------
        // Program assets (pattern: PocketConveniInstaller.cs EnsureProgramAsset)
        // ------------------------------------------------------------------
        private static void EnsureProgramAssets()
        {
            bool createdCore = EnsureProgramAsset(CorePath);
            bool createdDisplay = EnsureProgramAsset(DisplayPath);
            bool createdAudio = EnsureProgramAsset(AudioPath);
            bool createdBridge = EnsureProgramAsset(BridgePath);
            bool created = createdCore || createdDisplay || createdAudio || createdBridge;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = true });
            AssetDatabase.SaveAssets();
            if (UdonSharpProgramAsset.GetProgramAssetForClass(typeof(RotationAlertCore)) == null ||
                UdonSharpProgramAsset.GetProgramAssetForClass(typeof(RotationAlertDisplay)) == null ||
                UdonSharpProgramAsset.GetProgramAssetForClass(typeof(RotationAlertAudio)) == null ||
                UdonSharpProgramAsset.GetProgramAssetForClass(typeof(RotationAlertShaderBridge)) == null)
                throw new System.InvalidOperationException(
                    "[RotationAlert] Failed to associate one or more UdonSharp program assets.");
            if (created)
                throw new System.InvalidOperationException(
                    "[RotationAlert] Created a UdonSharp program asset for the first time. " +
                    "Run this menu item once more to install.");
        }

        private static bool EnsureProgramAsset(string pathWithoutExtension)
        {
            string scriptPath = pathWithoutExtension + ".cs";
            string assetPath = pathWithoutExtension + ".asset";
            MonoScript sourceScript = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
            if (sourceScript == null)
                throw new System.InvalidOperationException("[RotationAlert] Missing UdonSharp source: " + scriptPath);
            UdonSharpProgramAsset programAsset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(assetPath);
            if (programAsset == null)
            {
                programAsset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                programAsset.sourceCsScript = sourceScript;
                AssetDatabase.CreateAsset(programAsset, assetPath);
                return true;
            }
            if (programAsset.sourceCsScript != sourceScript)
            {
                programAsset.sourceCsScript = sourceScript;
                EditorUtility.SetDirty(programAsset);
            }
            return false;
        }

        // ------------------------------------------------------------------
        // Font (pattern: PocketOverdriveInstaller.Assets.cs EnsureJapaneseFontAsset)
        // ------------------------------------------------------------------
        private static TMP_FontAsset EnsureFontAsset()
        {
            TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (fontAsset == null)
            {
                Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(FontSourcePath);
                if (sourceFont == null)
                    throw new System.InvalidOperationException(
                        "[RotationAlert] Missing required font source: " + FontSourcePath);

                if (Shader.Find("TextMeshPro/Mobile/Distance Field") == null)
                    throw new System.InvalidOperationException(
                        "[RotationAlert] TextMeshPro Essential Resources are not imported, so the Japanese font " +
                        "asset cannot be created. Run Window > TextMeshPro > Import TMP Essential Resources, then install again.");

                fontAsset = TMP_FontAsset.CreateFontAsset(sourceFont);
                fontAsset.name = "RotationAlert JP SDF";
                fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                AssetDatabase.CreateAsset(fontAsset, FontAssetPath);

                Texture2D atlas = fontAsset.atlasTexture;
                if (atlas != null)
                {
                    atlas.name = "RotationAlert JP Atlas";
                    AssetDatabase.AddObjectToAsset(atlas, fontAsset);
                }

                Material material = fontAsset.material;
                if (material != null)
                {
                    material.name = "RotationAlert JP Material";
                    AssetDatabase.AddObjectToAsset(material, fontAsset);
                }
            }

            // Always top up the character set, even on an existing asset, so a newly added
            // label is never silently missing its glyphs (Dynamic mode allows this at any time).
            fontAsset.TryAddCharacters(AllUiCharacters);
            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();
            return fontAsset;
        }

        // ------------------------------------------------------------------
        // Generated fill sprite — a plain white square. uGUI's Image ignores `type = Filled`
        // when `sprite == null` (it falls back to a plain quad and fillAmount does nothing),
        // so the progress bar needs a real (serialized, not runtime Sprite.Create) sprite asset.
        // ------------------------------------------------------------------
        private static Sprite EnsureFillSprite()
        {
            Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(FillSpritePath);
            if (existing != null) return existing;

            string fullPath = ProjectAbsolutePath(FillSpritePath);
            string directory = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

            Texture2D texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[8 * 8];
            Color32 white = new Color32(255, 255, 255, 255);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = white;
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(fullPath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(FillSpritePath, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(FillSpritePath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(FillSpritePath);
        }

        private static Material EnsureUiMaterial(string path, string name)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("UI/Default");
                if (shader == null) throw new System.InvalidOperationException("[RotationAlert] Missing built-in UI/Default shader.");
                material = new Material(shader);
                material.name = name;
                AssetDatabase.CreateAsset(material, path);
            }
            material.renderQueue = 3000;
            material.SetInt("unity_GUIZTestMode", (int)CompareFunction.LessEqual);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material EnsureTmpMaterial(TMP_FontAsset font, string path, string name)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                if (font == null || font.material == null) throw new System.InvalidOperationException("[RotationAlert] Missing TMP font material source.");
                material = new Material(font.material);
                material.name = name;
                AssetDatabase.CreateAsset(material, path);
            }
            material.renderQueue = 3000;
            material.SetInt("unity_GUIZTestMode", (int)CompareFunction.LessEqual);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static string ProjectAbsolutePath(string assetPath)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(projectRoot, assetPath);
        }

        private static void EnsureDirectories()
        {
            if (!AssetDatabase.IsValidFolder(RootDir.TrimEnd('/')))
                throw new System.InvalidOperationException("[RotationAlert] Missing root folder: " + RootDir);
            if (!AssetDatabase.IsValidFolder(PrefabDir.TrimEnd('/')))
                AssetDatabase.CreateFolder(RootDir.TrimEnd('/'), "Prefabs");
            if (!AssetDatabase.IsValidFolder(GeneratedDir.TrimEnd('/')))
                AssetDatabase.CreateFolder(RootDir.TrimEnd('/'), "Generated");
        }

        // ------------------------------------------------------------------
        // Audio clip loading (SPEC §2, §5) — warns, never throws, on a missing clip.
        // ------------------------------------------------------------------
        private static readonly string[] CueClipNames =
        {
            "rotation_start.wav",
            "warning.wav",
            "rotation_end.wav",
            null,
            "rotation_end.wav",
        };

        private static AudioClip[] LoadCueClips()
        {
            AudioClip[] clips = new AudioClip[5];
            for (int i = 0; i < CueClipNames.Length; i++)
            {
                if (CueClipNames[i] == null)
                {
                    clips[i] = null;
                    continue;
                }
                string path = AudioDir + CueClipNames[i];
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null)
                    Debug.LogWarning("[RotationAlert] Missing cue clip (will stay unassigned): " + path);
                clips[i] = clip;
            }
            return clips;
        }

        private static AudioClip LoadUiClickClip()
        {
            string path = AudioDir + "click.wav";
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null)
                Debug.LogWarning("[RotationAlert] Missing UI click clip (will stay unassigned): " + path);
            return clip;
        }

        // ------------------------------------------------------------------
        // Panel prefab (SPEC §3, §5, §6)
        // ------------------------------------------------------------------
        private static void BuildPanelPrefab(TMP_FontAsset font, Material uiMaterial, Material tmpMaterial)
        {
            DestroyLeftoverBuildRoot(PanelBuildRootName);
            Sprite fillSprite = EnsureFillSprite();

            GameObject root = new GameObject(PanelBuildRootName);

            AudioSource audioSource = root.AddComponent<AudioSource>();
            audioSource.spatialBlend = 0f;
            audioSource.playOnAwake = false;
            audioSource.volume = 1f;
            audioSource.loop = false;
            VRCSpatialAudioSource spatial = root.AddComponent<VRCSpatialAudioSource>();
            spatial.EnableSpatialization = false;

            RotationAlertCore coreProxy = root.AddUdonSharpComponent<RotationAlertCore>();
            RotationAlertAudio audioProxy = root.AddUdonSharpComponent<RotationAlertAudio>();
            RotationAlertDisplay displayProxy = root.AddUdonSharpComponent<RotationAlertDisplay>();
            RotationAlertShaderBridge bridgeProxy = root.AddUdonSharpComponent<RotationAlertShaderBridge>();
            bridgeProxy.core = coreProxy;

            UdonBehaviour coreBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(coreProxy);
            UdonBehaviour audioBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(audioProxy);
            if (coreBacking == null || audioBacking == null)
                throw new System.InvalidOperationException("[RotationAlert] Failed to find a backing UdonBehaviour.");

            coreProxy.defaultRotationMinutes = 15;
            coreProxy.defaultIntervalMinutes = 2;
            coreProxy.defaultRotationCount = 3;
            coreProxy.defaultWarnMinutes = 3;
            coreProxy.preStartSec = 30;
            coreProxy.listeners = new UdonBehaviour[0];

            audioProxy.core = coreProxy;
            audioProxy.source = audioSource;
            audioProxy.cueClips = LoadCueClips();
            audioProxy.cueVolumes = new float[] { 1f, 1f, 1f, 0f, 1f };
            audioProxy.cueRepeats = new int[] { 1, 1, 1, 1, 1 };
            audioProxy.repeatGap = 0.6f;
            audioProxy.muted = false;
            audioProxy.uiClick = LoadUiClickClip();
            audioProxy.uiClickVolume = 0.35f;

            Canvas canvas = CreateCanvas(root.transform, "Canvas", new Vector2(720f, 460f));

            // Background
            CreateImage(canvas.transform, "Background", new Vector2(720f, 460f), Vector2.zero, TokenBackground, false);

            // Accent strip (SPEC §6: 720x8 at y=+226)
            Image accentStrip = CreateImage(canvas.transform, "Accent Strip", new Vector2(720f, 8f), new Vector2(0f, 226f), TokenIdle, false);

            // phaseText (28pt left-aligned; shares the run-row left edge)
            TMP_Text phaseText = CreateText(canvas.transform, "Phase Text", RuntimeIdle, 28f,
                new Vector2(520f, 40f), new Vector2(-80f, 190f), TokenText, TextAlignmentOptions.Left, font, false, false);

            // Mute button (130x36 at (275, 190))
            Button muteButton = CreateButtonVisual(canvas.transform, "Mute Button", LabelMuteOn,
                new Vector2(130f, 36f), new Vector2(275f, 190f), TokenPrimary, font, 16f);
            TMP_Text muteLabel = muteButton.GetComponentInChildren<TextMeshProUGUI>();
            AddOpListener(muteButton, audioBacking, "ToggleMute");
            AddClickListener(muteButton, audioBacking);

            // timeText (120pt centered at (0, 95) size 600x140)
            TMP_Text timeText = CreateText(canvas.transform, "Time Text", "00:00", 120f,
                new Vector2(600f, 140f), new Vector2(0f, 95f), TokenText, TextAlignmentOptions.Center, font, false, false);

            // progress track (640x16 at (0, 10) dark) + fill child (teal, Filled Horizontal)
            Image progressTrack = CreateImage(canvas.transform, "Progress Track", new Vector2(640f, 16f), new Vector2(0f, 10f), TokenSurface, false);
            GameObject fillObject = new GameObject("Progress Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            fillObject.transform.SetParent(progressTrack.transform, false);
            RectTransform fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.sizeDelta = new Vector2(640f, 16f);
            fillRect.anchoredPosition = Vector2.zero;
            Image progressFill = fillObject.GetComponent<Image>();
            progressFill.sprite = fillSprite;
            progressFill.type = Image.Type.Filled;
            progressFill.fillMethod = Image.FillMethod.Horizontal;
            progressFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            progressFill.fillAmount = 0f;
            progressFill.color = TokenIdle;
            progressFill.raycastTarget = false;

            // subText (20pt centered at (0, -22) size 640x30)
            TMP_Text subText = CreateText(canvas.transform, "Sub Text", RuntimeIdleSummary, 20f,
                new Vector2(640f, 30f), new Vector2(0f, -22f), TokenMuted, TextAlignmentOptions.Center, font, false, false);

            // divider at y=-45
            CreateImage(canvas.transform, "Divider Top", new Vector2(680f, 2f), new Vector2(0f, -45f), WithAlpha(TokenMuted, 0.35f), false);

            // run row buttons at y=-85. The row is centered in a 680px content area
            // (20px inset on both sides of the 720px canvas).
            Button startButton = CreateButtonVisual(canvas.transform, "Start Button", LabelStart,
                new Vector2(100f, 56f), new Vector2(-290f, -85f), TokenPrimary, font, 20f);
            AddOpListener(startButton, coreBacking, "OpStart");
            AddClickListener(startButton, audioBacking);

            Button pauseButton = CreateButtonVisual(canvas.transform, "Pause Button", LabelPause,
                new Vector2(100f, 56f), new Vector2(-176f, -85f), TokenSurface, font, 20f);
            AddOpListener(pauseButton, coreBacking, "OpPauseResume");
            AddClickListener(pauseButton, audioBacking);
            TMP_Text pauseLabel = pauseButton.GetComponentInChildren<TextMeshProUGUI>();

            Button skipButton = CreateButtonVisual(canvas.transform, "Skip Button", LabelSkip,
                new Vector2(100f, 56f), new Vector2(-62f, -85f), TokenSurface, font, 20f);
            AddOpListener(skipButton, coreBacking, "OpSkip");
            AddClickListener(skipButton, audioBacking);
            TMP_Text skipLabel = skipButton.GetComponentInChildren<TextMeshProUGUI>();

            Button plusMinuteButton = CreateButtonVisual(canvas.transform, "Plus Minute Button", LabelPlusMinute,
                new Vector2(100f, 56f), new Vector2(52f, -85f), TokenSurface, font, 20f);
            AddOpListener(plusMinuteButton, coreBacking, "OpPlusMinute");
            AddClickListener(plusMinuteButton, audioBacking);

            Button minusMinuteButton = CreateButtonVisual(canvas.transform, "Minus Minute Button", LabelMinusMinute,
                new Vector2(100f, 56f), new Vector2(166f, -85f), TokenSurface, font, 20f);
            AddOpListener(minusMinuteButton, coreBacking, "OpMinusMinute");
            AddClickListener(minusMinuteButton, audioBacking);

            Button resetButton = CreateButtonVisual(canvas.transform, "Reset Button", LabelReset,
                new Vector2(110f, 56f), new Vector2(285f, -85f), TokenDanger, font, 20f);
            AddOpListener(resetButton, coreBacking, "OpResetRequest");
            AddClickListener(resetButton, audioBacking);
            TMP_Text resetLabel = resetButton.GetComponentInChildren<TextMeshProUGUI>();

            // divider at y=-120
            CreateImage(canvas.transform, "Divider Bottom", new Vector2(680f, 2f), new Vector2(0f, -120f), WithAlpha(TokenMuted, 0.35f), false);

            // settings caption (decorative, not a Display field) + settingsText (SPEC §4)
            CreateText(canvas.transform, "Settings Caption", LabelSettingsCaption, 14f,
                new Vector2(70f, 26f), new Vector2(-305f, -140f), TokenMuted, TextAlignmentOptions.Left, font, false, false);
            TMP_Text settingsText = CreateText(canvas.transform, "Settings Text", RuntimeSettingsLine, 18f,
                new Vector2(600f, 26f), new Vector2(30f, -140f), TokenMuted, TextAlignmentOptions.Center, font, false, false);

            // setting row at y=-185: four groups at x=-255,-85,85,255
            Button[] settingButtons = new Button[8];
            settingButtons[0] = CreateSettingGroup(canvas.transform, -255f, -185f, LabelSettingRotation,
                coreBacking, "OpRotationMinus", "OpRotationPlus", audioBacking, font, out settingButtons[1]);
            settingButtons[2] = CreateSettingGroup(canvas.transform, -85f, -185f, LabelSettingInterval,
                coreBacking, "OpIntervalMinus", "OpIntervalPlus", audioBacking, font, out settingButtons[3]);
            settingButtons[4] = CreateSettingGroup(canvas.transform, 85f, -185f, LabelSettingCount,
                coreBacking, "OpCountMinus", "OpCountPlus", audioBacking, font, out settingButtons[5]);
            settingButtons[6] = CreateSettingGroup(canvas.transform, 255f, -185f, LabelSettingWarn,
                coreBacking, "OpWarnMinus", "OpWarnPlus", audioBacking, font, out settingButtons[7]);

            displayProxy.core = coreProxy;
            displayProxy.alertAudio = audioProxy;
            displayProxy.phaseText = (TextMeshProUGUI)phaseText;
            displayProxy.timeText = (TextMeshProUGUI)timeText;
            displayProxy.subText = (TextMeshProUGUI)subText;
            displayProxy.settingsText = (TextMeshProUGUI)settingsText;
            displayProxy.pauseButtonLabel = (TextMeshProUGUI)pauseLabel;
            displayProxy.skipButtonLabel = (TextMeshProUGUI)skipLabel;
            displayProxy.resetButtonLabel = (TextMeshProUGUI)resetLabel;
            displayProxy.muteButtonLabel = (TextMeshProUGUI)muteLabel;
            displayProxy.uiRaycaster = canvas.GetComponent<GraphicRaycaster>();
            displayProxy.interactRange = 1f;
            displayProxy.progressFill = progressFill;
            displayProxy.accentImages = new Image[] { accentStrip, progressFill };
            displayProxy.startButton = startButton;
            displayProxy.runControlButtons = new Button[] { pauseButton, skipButton, plusMinuteButton, minusMinuteButton };
            displayProxy.settingButtons = settingButtons;

            Image[] images = root.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++) images[i].material = uiMaterial;

            TextMeshProUGUI[] texts = root.GetComponentsInChildren<TextMeshProUGUI>(true);
            for (int i = 0; i < texts.Length; i++) texts[i].fontSharedMaterial = tmpMaterial;

            UdonSharpEditorUtility.CopyProxyToUdon(coreProxy);
            UdonSharpEditorUtility.CopyProxyToUdon(audioProxy);
            UdonSharpEditorUtility.CopyProxyToUdon(displayProxy);
            UdonSharpEditorUtility.CopyProxyToUdon(bridgeProxy);

            root.name = "RotationAlert Panel";
            PrefabUtility.SaveAsPrefabAsset(root, PanelPrefabPath);
            Object.DestroyImmediate(root);
        }

        // Builds one "-"/"+" pair plus its label, wired to core/audio, returns the minus Button
        // and hands back the plus Button via out-parameter (keeps the eight-button call sites short).
        private static Button CreateSettingGroup(Transform parent, float groupCenterX, float y, string label,
            UdonBehaviour coreBacking, string minusEvent, string plusEvent, UdonBehaviour audioBacking,
            TMP_FontAsset font, out Button plusButton)
        {
            CreateText(parent, label + " Label", label, 18f, new Vector2(60f, 30f),
                new Vector2(groupCenterX - 50f, y), TokenText, TextAlignmentOptions.Center, font, false, false);

            Button minusButton = CreateButtonVisual(parent, label + " Minus Button", LabelMinusGlyph,
                new Vector2(44f, 44f), new Vector2(groupCenterX + 4f, y), TokenSurface, font, 22f);
            AddOpListener(minusButton, coreBacking, minusEvent);
            AddClickListener(minusButton, audioBacking);

            plusButton = CreateButtonVisual(parent, label + " Plus Button", LabelPlusGlyph,
                new Vector2(44f, 44f), new Vector2(groupCenterX + 54f, y), TokenSurface, font, 22f);
            AddOpListener(plusButton, coreBacking, plusEvent);
            AddClickListener(plusButton, audioBacking);

            return minusButton;
        }

        // ------------------------------------------------------------------
        // Display-only prefab (SPEC §6: 720x300, accent + phase + time + progress + sub only)
        // ------------------------------------------------------------------
        private static void BuildDisplayPrefab(TMP_FontAsset font, Material uiMaterial, Material tmpMaterial)
        {
            DestroyLeftoverBuildRoot(DisplayBuildRootName);
            Sprite fillSprite = EnsureFillSprite();

            GameObject root = new GameObject(DisplayBuildRootName);
            RotationAlertDisplay displayProxy = root.AddUdonSharpComponent<RotationAlertDisplay>();

            Canvas canvas = CreateCanvas(root.transform, "Canvas", new Vector2(720f, 300f));
            CreateImage(canvas.transform, "Background", new Vector2(720f, 300f), Vector2.zero, TokenBackground, false);

            Image accentStrip = CreateImage(canvas.transform, "Accent Strip", new Vector2(720f, 8f), new Vector2(0f, 146f), TokenIdle, false);

            TMP_Text phaseText = CreateText(canvas.transform, "Phase Text", RuntimeIdle, 24f,
                new Vector2(640f, 36f), new Vector2(-20f, 110f), TokenText, TextAlignmentOptions.Left, font, false, false);

            TMP_Text timeText = CreateText(canvas.transform, "Time Text", "00:00", 80f,
                new Vector2(600f, 90f), new Vector2(0f, 25f), TokenText, TextAlignmentOptions.Center, font, false, false);

            Image progressTrack = CreateImage(canvas.transform, "Progress Track", new Vector2(640f, 16f), new Vector2(0f, -60f), TokenSurface, false);
            GameObject fillObject = new GameObject("Progress Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            fillObject.transform.SetParent(progressTrack.transform, false);
            RectTransform fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.sizeDelta = new Vector2(640f, 16f);
            fillRect.anchoredPosition = Vector2.zero;
            Image progressFill = fillObject.GetComponent<Image>();
            progressFill.sprite = fillSprite;
            progressFill.type = Image.Type.Filled;
            progressFill.fillMethod = Image.FillMethod.Horizontal;
            progressFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            progressFill.fillAmount = 0f;
            progressFill.color = TokenIdle;
            progressFill.raycastTarget = false;

            TMP_Text subText = CreateText(canvas.transform, "Sub Text", RuntimeIdleSummary, 18f,
                new Vector2(640f, 28f), new Vector2(0f, -95f), TokenMuted, TextAlignmentOptions.Center, font, false, false);

            displayProxy.core = null;
            displayProxy.alertAudio = null;
            displayProxy.phaseText = (TextMeshProUGUI)phaseText;
            displayProxy.timeText = (TextMeshProUGUI)timeText;
            displayProxy.subText = (TextMeshProUGUI)subText;
            displayProxy.settingsText = null;
            displayProxy.pauseButtonLabel = null;
            displayProxy.skipButtonLabel = null;
            displayProxy.resetButtonLabel = null;
            displayProxy.muteButtonLabel = null;
            displayProxy.uiRaycaster = null;
            displayProxy.progressFill = progressFill;
            displayProxy.accentImages = new Image[] { accentStrip, progressFill };
            displayProxy.startButton = null;
            displayProxy.runControlButtons = new Button[0];
            displayProxy.settingButtons = new Button[0];

            Image[] images = root.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++) images[i].material = uiMaterial;

            TextMeshProUGUI[] texts = root.GetComponentsInChildren<TextMeshProUGUI>(true);
            for (int i = 0; i < texts.Length; i++) texts[i].fontSharedMaterial = tmpMaterial;

            UdonSharpEditorUtility.CopyProxyToUdon(displayProxy);

            root.name = "RotationAlert Display";
            PrefabUtility.SaveAsPrefabAsset(root, DisplayPrefabPath);
            Object.DestroyImmediate(root);
        }

        private static void DestroyLeftoverBuildRoot(string name)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            GameObject[] roots = activeScene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
                if (roots[i].name == name) Object.DestroyImmediate(roots[i]);
        }
    }
}
#endif
