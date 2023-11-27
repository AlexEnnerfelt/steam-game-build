using System;
using System.Collections.Generic;
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
		public List<BuildTask> buildTasks { get; set; }
		[field: SerializeField]
		public List<string> steamBuildScripts { get; set; }
		public UnityEvent<uint> appIdChanged;


		public bool HasWaitingTasks {
			get {
				foreach (var item in buildTasks) {
					if (item.IsUnfinished) {
						return true;
					}
				}
				return false;
			}
		}
		/// <summary>
		/// The full path of where the steam cli executable is located
		/// </summary>
		public string BuilderExecutable => $"{SteamContentBuilder}{builderExecutable}";

		public const string settingsPath = "Assets/Editor/GameBuildSettings.asset";
		public static BuildSettingsObject current;
		public void OnEnable() {
			current = this;
		}
		public int callbackOrder => 1000;

		public void ClearTasks() {
			buildTasks.Clear();
		}
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
		[SerializeField]
		public bool uploadOnBuild;
		[field: SerializeField]
		public uint SteamAppId { get; set; }
		[SerializeField]
		private string AppBuildScriptLocation;
		[field: SerializeField]
		public string locationPathName { get; set; }
		[field: SerializeField]
		public string assetBundleManifestPath { get; set; }
		[field: SerializeField]
		public string[] includedMaps;
		[field: SerializeField]
		public PlatformOptions[] Platforms { get; set; }


		public string AppBuildScriptPath => $"{BuildSettingsObject.current.SteamContentBuilder}{AppBuildScriptLocation}";
		public string GetFullPath(BuildTarget target) => $"{BuildSettingsObject.current.SteamContentBuilder}/{locationPathName}/{buildPlatformSubfolderPaths[target]}/{executableFileName[target]}";

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
		public string[] Scenes => includedMaps;
		public BuildPlayerOptions[] GetBuildOptions() {
			var options = new List<BuildPlayerOptions>();
			foreach (var opt in Platforms) {
				var option = new BuildPlayerOptions() {
					locationPathName = GetFullPath(opt.target),
					target = opt.target,
					options = opt.options,
					assetBundleManifestPath = assetBundleManifestPath,
					extraScriptingDefines = opt.extraScriptingDefines,
					scenes = Scenes,
					subtarget = opt.subtarget,
					targetGroup = opt.targetGroup
				};
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
		public BuildTask(BuildPlayerOptions opt, AdditionalBuildData add) {
			data = add;
			status = BuildTaskStatus.None;
			scenes = opt.scenes;
			locationPathName = opt.locationPathName;
			assetBundleManifestPath = opt.assetBundleManifestPath;
			targetGroup = opt.targetGroup;
			target = opt.target;
			subtarget = opt.subtarget;
			options = opt.options;
			extraScriptingDefines = opt.extraScriptingDefines;
		}
		public enum BuildTaskStatus { None, Building, Finished, Cancelled, Failed }
		public bool IsFinished => status is BuildTaskStatus.Finished;
		public bool IsUnfinished => status is BuildTaskStatus.None;
		public BuildTaskStatus status = BuildTaskStatus.None;
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
			return
				scenes.Equals(other.scenes) &&
				locationPathName.Equals(other.locationPathName) &&
				assetBundleManifestPath.Equals(other.assetBundleManifestPath) &&
				targetGroup.Equals(other.targetGroup) &&
				target.Equals(other.target) &&
				subtarget.Equals(other.subtarget) &&
				options.Equals(other.options) &&
				extraScriptingDefines.Equals(other.extraScriptingDefines);
		}
		public static implicit operator BuildPlayerOptions(BuildTask task) {
			return new BuildPlayerOptions {
				scenes = task.scenes,
				locationPathName = task.locationPathName,
				assetBundleManifestPath = task.assetBundleManifestPath,
				targetGroup = task.targetGroup,
				target = task.target,
				subtarget = task.subtarget,
				options = task.options,
				extraScriptingDefines = task.extraScriptingDefines
			};
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