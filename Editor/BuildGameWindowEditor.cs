using System;
using System.Linq;
using System.Text;
using Ennerfelt.GitVersioning;
using UnityEditor;
using UnityEngine.UIElements;

namespace Carnage.BuildEditor {
	public class BuildGameWindowEditor : EditorWindow {
		[MenuItem("Window/Game Build Window")]
		private static void ShowWindow() {
			var window = GetWindow<BuildGameWindowEditor>();
			window.minSize = new(430, 650);
			window.maxSize = new(430, 650);
			window.titleContent = new("Game Build");
		}

		private BuildConfiguration DemoBuild => BuildSettingsObject.current.GetBuildPlayerOptions(GameBuildContentType.Demo);
		private BuildConfiguration PlaytestBuild => BuildSettingsObject.current.GetBuildPlayerOptions(GameBuildContentType.Playtest);
		private BuildConfiguration ReleaseBuild => BuildSettingsObject.current.GetBuildPlayerOptions(GameBuildContentType.Release);


		private TextElement VersionLabel => rootVisualElement.Q<TextElement>("version-label");
		private TextElement BuildProgressLabel => rootVisualElement.Q<TextElement>("build-tasks");
		private ProgressBar BuildProgressBar => rootVisualElement.Q<ProgressBar>("build-progress");
		private VisualElement ProgressElement => rootVisualElement.Q<VisualElement>("progress");

		public void OnValidate() {
			SavePersistentValues();
		}
		public void CreateGUI() {
			GameBuildPipeline.FindSettings();

			var visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Packages/com.ennerfelt.steam-build-editor/Editor/GameBuildWindowDocument.uxml");
			var visualTree = visualTreeAsset.Instantiate();
			rootVisualElement.Add(visualTree);

			SetupButtons();
			SetUpBuildPaths();
			SetUpAppIds();
			SetupSteamLogin();
			UpdateAppVersion();
			UpdateProgressBar();
			SavePersistentValues();
			GameBuildPipeline.OnBuildProgressChanged += UpdateProgressBar;
		}
		public void OnDestroy() {
			GameBuildPipeline.OnBuildProgressChanged -= UpdateProgressBar;
			SavePersistentValues();
		}
		public void OnBecameVisible() {
			UpdateAppVersion();
			UpdateProgressBar();
		}
		public void OnFocus() {
			UpdateAppVersion();
			UpdateProgressBar();
			SavePersistentValues();
		}
		void SetupSteamLogin() {
			var steamLoginRoot = rootVisualElement.Q<VisualElement>("steam-login");
			var username = steamLoginRoot.Q<TextField>("username");
			var password = steamLoginRoot.Q<TextField>("password");
			username.SetValueWithoutNotify(SteamLoginInfo.current.username);
			username.RegisterValueChangedCallback(e => {
				SteamLoginInfo.current.username = e.newValue;
			});
			password.SetValueWithoutNotify(SteamLoginInfo.current.password);
			password.RegisterValueChangedCallback(e => {
				SteamLoginInfo.current.password = e.newValue;
			});
		}
		void SetupButtons() {
			SetUp(rootVisualElement.Q<Button>("button_build-all"), new BuildConfiguration[] { DemoBuild, PlaytestBuild, ReleaseBuild });
			SetUp(rootVisualElement.Q<Button>("button-demo"), new BuildConfiguration[] { DemoBuild });
			SetUp(rootVisualElement.Q<Button>("button-playtest"), new BuildConfiguration[] { PlaytestBuild });
			SetUp(rootVisualElement.Q<Button>("button-release"), new BuildConfiguration[] { ReleaseBuild });

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
		void UpdateProgressBar() {
			try {
				ProgressElement.style.display = BuildSettingsObject.current.HasWaitingTasks ? DisplayStyle.Flex : DisplayStyle.None;
				var totalTasks = BuildSettingsObject.current.buildTasks.Count;
				var finishedTasks = BuildSettingsObject.current.buildTasks.Count(t => t.IsFinished);
				BuildProgressBar.highValue = totalTasks;
				BuildProgressBar.lowValue = 0;
				BuildProgressBar.value = finishedTasks + 1;
				BuildProgressBar.title = $"{finishedTasks} / {totalTasks}";
			} catch (NullReferenceException) { }
		}
		void SavePersistentValues() {
			EditorUtility.SetDirty(BuildSettingsObject.current);
			EditorUtility.SetDirty(SteamLoginInfo.current);
			AssetDatabase.SaveAssetIfDirty(BuildSettingsObject.current);
			AssetDatabase.SaveAssetIfDirty(SteamLoginInfo.current);
			AssetDatabase.Refresh();
			//TODO Make this reimport so the values stay 

		}
	}
}