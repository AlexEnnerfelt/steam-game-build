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
			get => BuildSettingsObject.current.buildTasks;
			private set => BuildSettingsObject.current.buildTasks = value;
		}

		internal static BuildTarget currentTarget => EditorUserBuildSettings.activeBuildTarget;
		internal const string k_AppIdFilename = "steam_appid.txt";

		public static event Action OnBuildProgressChanged;
		private static CancellationTokenSource _cts;
		static GameBuildPipeline() {
			var settingsAsset = AssetDatabase.LoadAssetAtPath<BuildSettingsObject>(BuildSettingsObject.settingsPath);
			if (settingsAsset == null) {
				AssetDatabase.CreateAsset(ScriptableObject.CreateInstance(typeof(BuildSettingsObject)), BuildSettingsObject.settingsPath);
				settingsAsset = AssetDatabase.LoadAssetAtPath<BuildSettingsObject>(BuildSettingsObject.settingsPath);
			}
			BuildSettingsObject.current = settingsAsset;

			var steamlogin = AssetDatabase.LoadAssetAtPath<SteamLoginInfo>(SteamLoginInfo.k_Path);
			if (steamlogin == null) {
				AssetDatabase.CreateAsset(ScriptableObject.CreateInstance(typeof(SteamLoginInfo)), SteamLoginInfo.k_Path);
				steamlogin = AssetDatabase.LoadAssetAtPath<SteamLoginInfo>(SteamLoginInfo.k_Path);
			}
			SteamLoginInfo.current = steamlogin;
			StartBuildTasks();

			EditorApplication.quitting += ClearBuildTasks;
		}

		private static void ClearBuildTasks() {
			if (BuildSettingsObject.current.HasWaitingTasks) {
				BuildSettingsObject.current.ClearTasks();
				EditorUtility.SetDirty(BuildSettingsObject.current);
				AssetDatabase.SaveAssetIfDirty(BuildSettingsObject.current);
				AssetDatabase.Refresh();
			}
		}


		[MenuItem("Tools/Build/Prepare build Tasks")]
		public static void PrepareTasks() {
			ClearTasks();
			SetUpTasks(BuildSettingsObject.current.Builds);
			ChangeAppDescription(BuildSettingsObject.current.Builds[0]);
			PrepareBuildScripts(BuildSettingsObject.current.Builds);
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
			BuildSettingsObject.current.steamBuildScripts = buildScripts;
		}

		private static async void StartBuildTasks() {
			_cts = new();
			if (BuildSettingsObject.current.HasWaitingTasks) {
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
			var report = BuildPipeline.BuildPlayer(task);

			if (report.summary.result != BuildResult.Succeeded) {
				_cts.Cancel();
				CancelAllTasks();
				return;
			}

			WriteAppIdFile(task);
			SetBuildTaskAsFinished(task);
			OnBuildProgressChanged?.Invoke();
		}
		private static async Task SwitchTarget(BuildTarget target) {
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
			BuildSettingsObject.current.steamBuildScripts.Clear();
			EditorUtility.SetDirty(BuildSettingsObject.current);
			AssetDatabase.SaveAssetIfDirty(BuildSettingsObject.current);
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
		private static void SetBuildTaskAsFinished(BuildPlayerOptions options) {
			var buildTask = BuildTasks.Find(c => c.Equals(options));
			buildTask.status = BuildTask.BuildTaskStatus.Finished;
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
			var settings = BuildSettingsObject.current.SteamSettings;
			settings.applicationId = new(steamAppId);
			EditorUtility.SetDirty(settings);
			AssetDatabase.SaveAssetIfDirty(settings);
		}
		private static void SetUpTasks(BuildConfiguration[] buildOptions) {
			var tasks = new List<BuildTask>();

			foreach (var buildOptionReference in buildOptions) {
				foreach (var buildPlayerOption in buildOptionReference.GetBuildOptions()) {

					var additionalData = new AdditionalBuildData() {
						appId = buildOptionReference.SteamAppId,
						executableName = executableFileName[buildPlayerOption.target],
						locationPathName = $"{BuildSettingsObject.current.SteamContentBuilder}{buildOptionReference.locationPathName}",
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
			if (BuildSettingsObject.current.steamBuildScripts.Count < 1) {
				return;
			}
			var cli = BuildSettingsObject.current.BuilderExecutable;
			var loginArgs = $" +login {SteamLoginInfo.current.username} {SteamLoginInfo.current.password}";
			string buildArguments = null;
			foreach (var buildScript in BuildSettingsObject.current.steamBuildScripts) {
				buildArguments += $" +run_app_build {buildScript}";
			}
			var fullArgs = $"{loginArgs}{buildArguments}";
			BuildSettingsObject.current.steamBuildScripts.Clear();
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