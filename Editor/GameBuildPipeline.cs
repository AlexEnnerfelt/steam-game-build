using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ennerfelt.GitVersioning;
using NUnit.Framework;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;


namespace Carnage.BuildEditor {
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

		internal static BuildTarget currentTarget => EditorUserBuildSettings.activeBuildTarget;
		internal const string k_AppIdFilename = "steam_appid.txt";
		internal const string k_SteamUsername = "steam_username";
		internal const string k_SteamPassword = "steam_password";

		public static event Action OnBuildProgressChanged;
		private static CancellationTokenSource _cts;

		internal static void RequestBuild(BuildConfiguration[] buildOptions) {
			var tasks = SetUpTasks(buildOptions);
			ChangeAppDescription(buildOptions);
			PrepareBuildScripts(buildOptions);
			EditorCoroutineUtility.StartCoroutine(BuildTasksCoroutine(tasks), BuildSettingsObject.Current);
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

		public static IEnumerator BuildTasksCoroutine(List<BuildTask> tasks) {
			for (var i = 0; i < Progress.GetCount(); i++) {
				Progress.Remove(Progress.GetId(i));
			}

			//Take all the different platforms
			var distinctPlatforms = tasks.Select(t => t.target).Distinct().ToList();

			var platformProgressIds = new Dictionary<BuildTarget, int>();
			var buildTasks = new Dictionary<BuildTarget, List<BuildTask>>();
			var buildTaskProgressIds = new Dictionary<BuildTask, int>();

			//Set up all the progressbars
			foreach (var platform in distinctPlatforms) {
				var id = Progress.Start($"<b>{TargetNameStrings[platform]}</b> Builds ", "Waiting to build", Progress.Options.Sticky);
				platformProgressIds[platform] = id;
				buildTasks[platform] = tasks.Where(t => t.target == platform).ToList();
				foreach (var task in buildTasks[platform]) {
					buildTaskProgressIds[task] = Progress.Start($"{task.name}", "Waiting", Progress.Options.Sticky, id);
				}
			}
			distinctPlatforms.Reverse();
			//Show progress
			Progress.ShowDetails();
			yield return new EditorWaitForSeconds(0.5f);

			//Go over all of the progressbars distinct to platforms
			foreach (var (platform, parentId) in platformProgressIds) {

				//Go over all of the buildtasks
				var index = 1;
				foreach (var task in buildTasks[platform]) {
					var taskProgressId = buildTaskProgressIds[task];
					Progress.Report(parentId, index, buildTasks[platform].Count, "Building");

					//Execute the actual build
					var buildResult = Build(task);

					//If it was anything other than a success, cancel the whole task
					if (buildResult != BuildResult.Succeeded) {
						foreach (var t in tasks) {
							if (!t.IsFinished) {
								Progress.Report(buildTaskProgressIds[t], 1f, "Failed");
								Progress.Finish(buildTaskProgressIds[t], (Progress.Status)buildResult);
							}
						}

						Progress.Finish(parentId, (Progress.Status)buildResult);
						yield break;
					}

					Progress.Report(taskProgressId, 1f, "Finished");
					Progress.Finish(taskProgressId, Progress.Status.Succeeded);


					index++;
					yield return new EditorWaitForSeconds(0.5f);
				}
				Progress.Finish(parentId, Progress.Status.Succeeded);

			}

			RunSteamBuilder();
		}

		private static readonly Dictionary<BuildTarget, string> TargetNameStrings = new() {
			{BuildTarget.StandaloneLinux64, "Linux" },
			{BuildTarget.StandaloneWindows64, "Windows" },
			{BuildTarget.StandaloneOSX, "OSX" },
		};

		private static BuildResult Build(BuildTask task) {
			SetSteamSettings(task.data.appId);
			var report = BuildPipeline.BuildPlayer(task.ToBuildPlayerOptions());
			task.status = report.summary.result;
			if (report.summary.result == BuildResult.Succeeded) {
				WriteAppIdFile(task);
			}
			return report.summary.result;

		}

		private static BuildResult BuildDebug(BuildTask task) {
			return BuildResult.Succeeded;
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
		private static void SetSteamSettings(uint steamAppId) {
			BuildSettingsObject.Current.appIdChanged.Invoke(steamAppId);
		}

		private static List<BuildTask> SetUpTasks(BuildConfiguration[] buildOptions) {
			var tasks = new List<BuildTask>();

			foreach (var buildOptionReference in buildOptions) {
				foreach (var buildPlayerOption in buildOptionReference.GetBuildOptions()) {

					var additionalData = new AdditionalBuildData() {
						appId = buildOptionReference.SteamAppId,
						executableName = executableFileName[buildPlayerOption.target],
						locationPathName = $"{BuildSettingsObject.Current.SteamContentBuilder}{buildOptionReference.locationPathName}",
						subfolderName = buildPlatformSubfolderPaths[buildPlayerOption.target]
					};

					tasks.Add(new(buildPlayerOption, additionalData, $"{buildOptionReference.Name}"));
				}
			}

			return tasks;
		}

		private static void RunSteamBuilder() {
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

		internal static void OnActiveBuildTargetChanged(BuildTarget previousTarget, BuildTarget newTarget) {

		}
		internal static void OnPreprocessBuild(BuildReport report) {

		}
		internal static void OnPostprocessBuild(BuildReport report) {

		}
	}
}