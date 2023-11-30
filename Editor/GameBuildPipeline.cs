using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ennerfelt.GitVersioning;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Carnage.BuildEditor {
	[InitializeOnLoad]
	public static class GameBuildPipeline {
		internal static readonly Dictionary<BuildTarget, string> buildPlatformSubfolderPaths = new() {
			{BuildTarget.StandaloneLinux64, "Linux"},
			{BuildTarget.StandaloneWindows64, "Windows"},
			{BuildTarget.StandaloneOSX, "Mac"}
		};
		internal static readonly Dictionary<BuildTarget, string> executableFileName = new() {
			{BuildTarget.StandaloneLinux64, "Carnage.x86_64"},
			{BuildTarget.StandaloneWindows64, "Carnage.exe"},
			{BuildTarget.StandaloneOSX, "Mac"}
		};
		internal static List<BuildTask> BuildTasks {
			get => BuildSettingsObject.Current.buildTasks;
			private set => BuildSettingsObject.Current.buildTasks = value;
		}

		internal static BuildTarget currentTarget => EditorUserBuildSettings.activeBuildTarget;
		internal const string k_AppIdFilename = "steam_appid.txt";
		internal const string k_SteamUsername = "steam_username";
		internal const string k_SteamPassword = "steam_password";

		public static event Action OnBuildProgressChanged;
		private static CancellationTokenSource _cts;
		static GameBuildPipeline() {
			EditorApplication.quitting += ClearBuildTasks;
		}

		private static void ClearBuildTasks() {
			BuildSettingsObject.Current.ClearTasks();
			EditorUtility.SetDirty(BuildSettingsObject.Current);
			AssetDatabase.SaveAssetIfDirty(BuildSettingsObject.Current);
			AssetDatabase.Refresh();
		}


		[MenuItem("Tools/Build/Prepare build Tasks")]
		public static void PrepareTasks() {
			ClearTasks();
			SetUpTasks(BuildSettingsObject.Current.Builds);
			ChangeAppDescription(BuildSettingsObject.Current.Builds[0]);
			PrepareBuildScripts(BuildSettingsObject.Current.Builds);
		}
		internal async static void RequestBuild(BuildConfiguration[] buildOptions) {
			ClearTasks();
			SetUpTasks(buildOptions);
			await Task.Yield();
			ChangeAppDescription(buildOptions);
			PrepareBuildScripts(buildOptions);
			StartBuildTasks();
		}
		private static void PrepareBuildScripts(BuildConfiguration[] buildOptions) {
			var buildScripts = new List<string>();
			foreach (var item in buildOptions) {
				if (item.uploadOnBuild) {
					buildScripts.Add(item.AppBuildScriptPath);
				}
			}
			BuildSettingsObject.Current.steamBuildScripts = buildScripts;
		}

		private static async void StartBuildTasks() {
			_cts = new();
			if (BuildSettingsObject.Current.HasWaitingTasks) {
				var tasks = GetBuildTasksForPlatform(currentTarget);
				OnBuildProgressChanged?.Invoke();
				await Task.Yield();

				while (IsBusy()) {
					await Task.Yield();
				}
				//If there is not even one buildtask
				if (tasks.Length < 1) {
					//If there are unfinished tasks for a different target
					if (GetTargetForUnfinishedTasks(out var target)) {
						await SwitchTarget(target);
						return;
					}
				} else {
					Build(tasks);
				}
			} else {
				RunBatchfile();
			}
		}
		private static void Build(BuildTask[] tasks) {
			foreach (var task in tasks) {
				if (!_cts.IsCancellationRequested) {
					Build(task);
				} else {
					return;
				}
			}
			//When the currently initiated tasks are finished
			StartBuildTasks();
		}
		private static void Build(BuildTask task) {
			SetSteamSettings(task.data.appId);
			var report = BuildPipeline.BuildPlayer(task.ToBuildPlayerOptions());

			if (report.summary.result != BuildResult.Succeeded) {
				_cts.Cancel();
				CancelAllTasks();
				return;
			}

			WriteAppIdFile(task);
			//SetBuildTaskAsFinished(task);
			task.status = BuildTask.BuildTaskStatus.Finished;
			OnBuildProgressChanged?.Invoke();
		}
		private static async Task SwitchTarget(BuildTarget target) {
			EditorUtility.SetDirty(BuildSettingsObject.Current);
			AssetDatabase.SaveAssetIfDirty(BuildSettingsObject.Current);
			AssetDatabase.Refresh();
			while (IsBusy()) {
				await Task.Yield();
			}
			EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, target);
			while (IsBusy()) {
				await Task.Yield();
			}
		}
		private static void CancelAllTasks() {
			foreach (var item in BuildTasks) {
				if (!item.IsFinished) {
					item.status = BuildTask.BuildTaskStatus.Cancelled;
					Debug.Log("Cancelled by user");
				}
			}
			BuildSettingsObject.Current.steamBuildScripts.Clear();
			EditorUtility.SetDirty(BuildSettingsObject.Current);
			AssetDatabase.SaveAssetIfDirty(BuildSettingsObject.Current);
		}
		private static void WriteAppIdFile(BuildTask task) {
			var fileLocation = task.AppIdFilePath;
			if (File.Exists(fileLocation)) {
				var fileText = File.ReadAllText(fileLocation);
				if (!fileText.Equals(task.AppId.ToString())) {

				}
			} else {
				File.WriteAllText(fileLocation, task.AppId.ToString());
			}
		}
		private static void ChangeAppDescription(BuildConfiguration[] builds) {
			foreach (var buildConfig in builds) {
				ChangeAppDescription(buildConfig);
			}
		}
		private static void ChangeAppDescription(BuildConfiguration build) {
			var file = File.ReadAllLines(build.AppBuildScriptPath);
			for (var i = 0; i < file.Length; i++) {
				if (file[i].Contains("\"Desc\"")) {
					file[i] = $"\t\"Desc\" \"{Git.BuildVersion}\"";
				}
			}
			File.WriteAllLines(build.AppBuildScriptPath, file);
		}
		private static bool GetTargetForUnfinishedTasks(out BuildTarget target) {
			BuildTask task = null;
			try {
				task = BuildTasks.First(task => !task.IsFinished && task.target != currentTarget);
			} catch (Exception) { }

			if (task is not null) {
				target = task.target;
				return true;
			} else {
				target = default;
				return false;
			}
		}
		private static void SetSteamSettings(uint steamAppId) {
			BuildSettingsObject.Current.appIdChanged.Invoke(steamAppId);
		}
		private static void SetUpTasks(BuildConfiguration[] buildOptions) {
			var tasks = new List<BuildTask>();

			foreach (var buildOptionReference in buildOptions) {
				foreach (var buildPlayerOption in buildOptionReference.GetBuildOptions()) {

					var additionalData = new AdditionalBuildData() {
						appId = buildOptionReference.SteamAppId,
						executableName = executableFileName[buildPlayerOption.target],
						locationPathName = $"{BuildSettingsObject.Current.SteamContentBuilder}{buildOptionReference.locationPathName}",
						subfolderName = buildPlatformSubfolderPaths[buildPlayerOption.target]
					};

					tasks.Add(new(buildPlayerOption, additionalData));
				}
			}

			BuildTasks = tasks;
		}
		private static BuildTask[] GetBuildTasksForPlatform(BuildTarget target) {
			var task = BuildTasks.Where(t => !t.IsFinished && t.target.Equals(target));
			return task.ToArray();
		}
		private static void ClearTasks() {
			BuildTasks.Clear();
		}

		private static bool IsBusy() {
			return BuildPipeline.isBuildingPlayer || EditorApplication.isCompiling || EditorApplication.isUpdating;
		}
		[MenuItem("Tools/Build/Prepare CLI Args")]
		private static void RunBatchfile() {
			if (BuildSettingsObject.Current.steamBuildScripts.Count < 1) {
				return;
			}
			var cli = BuildSettingsObject.Current.BuilderExecutable;
			var loginArgs = $" +login {EditorPrefs.GetString(k_SteamUsername)} {EditorPrefs.GetString(k_SteamPassword)}";
			string buildArguments = null;
			foreach (var buildScript in BuildSettingsObject.Current.steamBuildScripts) {
				buildArguments += $" +run_app_build {buildScript}";
			}
			var fullArgs = $"{loginArgs}{buildArguments}";
			BuildSettingsObject.Current.steamBuildScripts.Clear();
			Process.Start(cli, fullArgs);
		}

		internal static async void OnActiveBuildTargetChanged(BuildTarget previousTarget, BuildTarget newTarget) {
			await Task.Yield();
			while (IsBusy()) {
				await Task.Yield();
			}
			await Task.Yield();
			StartBuildTasks();
		}
		internal static void OnPreprocessBuild(BuildReport report) {
			//Check current build target
			//Set the version number correctly 
		}
		internal static void OnPostprocessBuild(BuildReport report) {
			//Check if there are any more build taks to finish
			//Check if they require a change to a new buildtarget
			//Execute the task
		}
	}
}