using System;
using System.Linq;
using System.Text;
using Ennerfelt.GitVersioning;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Carnage.BuildEditor {
	public class BuildGameWindowEditor : EditorWindow {
		[MenuItem("Window/Game Build Window")]
		private static void ShowWindow() {
			var window = GetWindow<BuildGameWindowEditor>();
			window.minSize = new(430, 650);
			window.maxSize = new(430, 650);
			window.titleContent = new("Game Build");
			ValidateObject(BuildSettingsObject.Current);
			window.CreateGUI();
		}

		private BuildConfiguration DemoBuild => BuildSettingsObject.Current.GetBuildPlayerOptions(GameBuildContentType.Demo);
		private BuildConfiguration PlaytestBuild => BuildSettingsObject.Current.GetBuildPlayerOptions(GameBuildContentType.Playtest);
		private BuildConfiguration ReleaseBuild => BuildSettingsObject.Current.GetBuildPlayerOptions(GameBuildContentType.Release);


		private TextElement VersionLabel => rootVisualElement.Q<TextElement>("version-label");
		private TextElement BuildProgressLabel => rootVisualElement.Q<TextElement>("build-tasks");
		private ProgressBar BuildProgressBar => rootVisualElement.Q<ProgressBar>("build-progress");
		private VisualElement ProgressElement => rootVisualElement.Q<VisualElement>("progress");

		public void OnValidate() {
			SavePersistentValues();
		}
		public void CreateGUI() {
			if (rootVisualElement == null) {
				return;
			}
			rootVisualElement.Clear();
			var visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Packages/com.ennerfelt.steam-build-editor/Editor/GameBuildWindowDocument.uxml");
			var visualTree = visualTreeAsset.Instantiate();
			rootVisualElement.Add(visualTree);

			SetupButtons();
			SetUpBuildPaths();
			SetUpAppIds();
			SetupSteamLogin();
			UpdateAppVersion();
			SavePersistentValues();
		}
		public void OnDestroy() {
			SavePersistentValues();
		}
		public void OnBecameVisible() {
			ValidateObject(BuildSettingsObject.Current);

			UpdateAppVersion();
		}
		public void OnFocus() {
			ValidateObject(BuildSettingsObject.Current);
			UpdateAppVersion();
			SavePersistentValues();
		}
		void SetupSteamLogin() {
			var steamLoginRoot = rootVisualElement.Q<VisualElement>("steam-login");
			var username = steamLoginRoot.Q<TextField>("username");
			var password = steamLoginRoot.Q<TextField>("password");

			username.SetValueWithoutNotify(EditorPrefs.GetString(GameBuildPipeline.k_SteamUsername));
			username.RegisterValueChangedCallback(e => {
				EditorPrefs.SetString(GameBuildPipeline.k_SteamUsername, e.newValue);
			});
			password.SetValueWithoutNotify(EditorPrefs.GetString(GameBuildPipeline.k_SteamPassword));
			password.RegisterValueChangedCallback(e => {
				EditorPrefs.SetString(GameBuildPipeline.k_SteamPassword, e.newValue);

			});
		}
		void SetupButtons() {
			try {
				SetUp(rootVisualElement.Q<Button>("button_build-all"), new BuildConfiguration[] { DemoBuild, PlaytestBuild, ReleaseBuild });
				SetUp(rootVisualElement.Q<Button>("button-demo"), new BuildConfiguration[] { DemoBuild });
				SetUp(rootVisualElement.Q<Button>("button-playtest"), new BuildConfiguration[] { PlaytestBuild });
				SetUp(rootVisualElement.Q<Button>("button-release"), new BuildConfiguration[] { ReleaseBuild });


			} catch (Exception) {

			}
			void SetUp(Button button, BuildConfiguration[] triggerBuilds) {
				var uploadToggle = button.Q<Toggle>();
				uploadToggle.SetValueWithoutNotify(triggerBuilds[0].uploadOnBuild);
				uploadToggle.RegisterValueChangedCallback(e => {
					ToggleUpload(e.newValue);
				});
				button.clicked += () => {
					ToggleUpload(uploadToggle.value);
					GameBuildPipeline.RequestBuild(triggerBuilds);
				};
				void ToggleUpload(bool value) {
					foreach (var item in triggerBuilds) {
						item.uploadOnBuild = value;
					}
				}
			}
		}
		void SetUpBuildPaths() {
			SetUp(rootVisualElement.Q<VisualElement>("path-demo"), DemoBuild);
			SetUp(rootVisualElement.Q<VisualElement>("path-playtest"), PlaytestBuild);
			SetUp(rootVisualElement.Q<VisualElement>("path-release"), ReleaseBuild);

			void SetUp(VisualElement parent, BuildConfiguration build) {
				var path = parent.Q<TextField>();
				var toggle = parent.Q<Toggle>();
				toggle.RegisterValueChangedCallback(e => {
					path.SetEnabled(e.newValue);
				});
				toggle.value = false;
				path.SetEnabled(false);
				path.SetValueWithoutNotify(build.locationPathName);
				path.RegisterValueChangedCallback(e => {
					StringBuilder sb = new();
					sb.Append(e.newValue);
					sb.Replace(@"\", "/");
					var final = sb.ToString();
					build.locationPathName = final;
				});
			}
		}
		void SetUpAppIds() {
			SetUp(rootVisualElement.Q<UnsignedIntegerField>("appid-demo"), DemoBuild);
			SetUp(rootVisualElement.Q<UnsignedIntegerField>("appid-playtest"), PlaytestBuild);
			SetUp(rootVisualElement.Q<UnsignedIntegerField>("appid-release"), ReleaseBuild);

			void SetUp(UnsignedIntegerField field, BuildConfiguration build) {
				field.SetValueWithoutNotify(build.SteamAppId);
				field.RegisterValueChangedCallback(e => {
					build.SteamAppId = e.newValue;
				});
			}
		}
		void UpdateAppVersion() {
			try {
				VersionLabel.text = $"{Git.BuildVersion} - {Git.Branch}";
			} catch (NullReferenceException) { }
		}

		static void ValidateObject(BuildSettingsObject obj) {
			if (obj != null) {

			}
		}

		void SavePersistentValues() {
			EditorUtility.SetDirty(BuildSettingsObject.Current);
			AssetDatabase.SaveAssetIfDirty(BuildSettingsObject.Current);
			AssetDatabase.Refresh();
		}
	}
}