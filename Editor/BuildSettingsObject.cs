using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Events;
using static Carnage.BuildEditor.GameBuildPipeline;

namespace Carnage.BuildEditor {
	public class BuildSettingsObject : ScriptableObject, IActiveBuildTargetChanged, IPreprocessBuildWithReport, IPostprocessBuildWithReport {
		[field: SerializeField]
		public string SteamContentBuilder { get; private set; }
		[field: SerializeField]
		private string builderExecutable { get; set; }
		public BuildConfiguration[] Builds;
		[field: SerializeField]
		public List<string> steamBuildScripts { get; set; }
		public UnityEvent<uint> appIdChanged;

		/// <summary>
		/// The full path of where the steam cli executable is located
		/// </summary>
		public string BuilderExecutable => $"{SteamContentBuilder}{builderExecutable}";

		public static string settingsPath => "Assets/Editor/GameBuildSettings.asset";
		public static BuildSettingsObject Current {
			get {
				if (_current == null) {
					_current = AssetDatabase.LoadAssetAtPath<BuildSettingsObject>(settingsPath);
					if (_current == null) {
						Debug.Log("No build settings found, created new");
						AssetDatabase.CreateAsset(CreateInstance(typeof(BuildSettingsObject)), settingsPath);
						_current = AssetDatabase.LoadAssetAtPath<BuildSettingsObject>(settingsPath);
					} else {
						return _current;
					}
				}
				return _current;
			}
			private set => _current = value;
		}
		private static BuildSettingsObject _current;

		public void OnEnable() {
			_current = this;
		}
		public void OnValidate() {
			Current = this;
		}
		public int callbackOrder => int.MaxValue;

		public BuildConfiguration GetBuildPlayerOptions(GameBuildContentType type) {
			foreach (var build in Builds) {
				if (build.ContentType == type) {
					return build;
				}
			}
			return null;
		}
		public void OnActiveBuildTargetChanged(BuildTarget previousTarget, BuildTarget newTarget) {
			GameBuildPipeline.OnActiveBuildTargetChanged(previousTarget, newTarget);
		}
		public void OnPreprocessBuild(BuildReport report) {

			GameBuildPipeline.OnPreprocessBuild(report);
		}
		public void OnPostprocessBuild(BuildReport report) {
			GameBuildPipeline.OnPostprocessBuild(report);
		}
	}

	[Serializable]
	public class BuildConfiguration {
		[SerializeField]
		public string Name;
		[field: SerializeField]
		public GameBuildContentType ContentType { get; set; }

		[Header("Steam Settings")]
		[SerializeField] public bool uploadOnBuild;
		[field: SerializeField] public uint SteamAppId { get; set; }
		[SerializeField] private string AppBuildScriptLocation;
		[field: SerializeField] public string locationPathName { get; set; }
		[field: SerializeField] public string assetBundleManifestPath { get; set; }
		[field: SerializeField]
		public string[] includedMaps;
		[field: SerializeField] public string[] extraScriptingDefines { get; set; }
		[field: SerializeField] public PlatformOptions[] Platforms { get; set; }

		public string AppBuildScriptPath => $"{BuildSettingsObject.Current.SteamContentBuilder}{AppBuildScriptLocation}";
		public string GetFullPath(BuildTarget target) => $"{BuildSettingsObject.Current.SteamContentBuilder}/{locationPathName}/{buildPlatformSubfolderPaths[target]}/{executableFileName[target]}";

		[Serializable]
		public class PlatformOptions {
			[field: SerializeField]
			public BuildTargetGroup targetGroup { get; set; }
			[field: SerializeField]
			public BuildTarget target { get; set; }
			[field: SerializeField]
			public int subtarget { get; set; }
			[field: SerializeField]
			public BuildOptions options { get; set; }
			[field: SerializeField]
			public string[] extraScriptingDefines { get; set; }
		}
		public BuildPlayerOptions[] GetBuildOptions() {
			var options = new List<BuildPlayerOptions>();
			foreach (var opt in Platforms) {

				var option = new BuildPlayerOptions();

				option.locationPathName = GetFullPath(opt.target);
				option.target = opt.target;
				option.options = opt.options;
				option.assetBundleManifestPath = assetBundleManifestPath;
				option.extraScriptingDefines = opt.extraScriptingDefines.Concat(extraScriptingDefines).ToArray();
				option.scenes = includedMaps;
				option.subtarget = opt.subtarget;
				option.targetGroup = opt.targetGroup;

				options.Add(option);
			}
			return options.ToArray();
		}
	}
	/// <summary>
	/// A task that contains all the data that is needed to serialize and survive a domain reload
	/// </summary>
	[Serializable]
	public class BuildTask : IEquatable<BuildPlayerOptions> {
		public BuildTask(BuildPlayerOptions opt, AdditionalBuildData add, string taskName = "default") {
			name = taskName;
			data = add;
			status = BuildResult.Unknown;
			scenes = opt.scenes;
			locationPathName = opt.locationPathName;
			assetBundleManifestPath = opt.assetBundleManifestPath;
			targetGroup = opt.targetGroup;
			target = opt.target;
			subtarget = opt.subtarget;
			options = opt.options;
			extraScriptingDefines = opt.extraScriptingDefines;
		}
		public BuildPlayerOptions ToBuildPlayerOptions() {
			var option = new BuildPlayerOptions();

			option.locationPathName = this.locationPathName;
			option.target = this.target;
			option.options = this.options;
			option.assetBundleManifestPath = assetBundleManifestPath;
			option.extraScriptingDefines = this.extraScriptingDefines;
			option.scenes = this.scenes;
			option.subtarget = this.subtarget;
			option.targetGroup = this.targetGroup;
			return option;
		}

		public bool IsFinished => status is BuildResult.Succeeded;
		public bool IsUnfinished => status is BuildResult.Unknown;
		public bool IsCancelled => status is BuildResult.Cancelled;
		public string name;
		public BuildResult status = BuildResult.Unknown;
		public string AppIdFilePath => $"{data.locationPathName}/{data.subfolderName}/{k_AppIdFilename}";
		public uint AppId => data.appId;
		public AdditionalBuildData data;
		public string[] scenes;
		public string locationPathName;
		public string assetBundleManifestPath;
		public BuildTargetGroup targetGroup;
		public BuildTarget target;
		public int subtarget;
		public BuildOptions options;
		public string[] extraScriptingDefines;

		public bool Equals(BuildPlayerOptions other) {
			var cnd1 = scenes.Equals(other.scenes);
			var cnd2 = locationPathName.Equals(other.locationPathName);
			var cnd3 = assetBundleManifestPath.Equals(other.assetBundleManifestPath);
			var cnd4 = targetGroup.Equals(other.targetGroup);
			var cnd5 = target.Equals(other.target);
			var cnd6 = subtarget.Equals(other.subtarget);
			var cnd7 = options.Equals(other.options);
			var cnd8 = extraScriptingDefines.Equals(other.extraScriptingDefines);

			var IsEqual = cnd1 && cnd2 && cnd3 && cnd4 && cnd5 && cnd6 && cnd7 && cnd8;
			return IsEqual;
		}
	}
	[Serializable]
	public struct AdditionalBuildData {
		public string locationPathName;
		public string subfolderName;
		public string executableName;
		public string steamAppScriptLocation;
		public uint appId;
	}
	public enum GameBuildContentType {
		Release = 0,
		Demo = 1,
		Playtest = 2
	}
}