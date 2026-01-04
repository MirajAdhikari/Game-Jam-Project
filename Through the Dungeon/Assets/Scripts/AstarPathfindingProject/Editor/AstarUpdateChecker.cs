using UnityEngine;
using UnityEditor;
#if UNITY_2018_1_OR_NEWER
using UnityEngine.Networking;
#endif
using System;
using System.Collections.Generic;
using System.Linq;

namespace Pathfinding {

	/// <summary>Handles update checking for the A* Pathfinding Project</summary>
	[InitializeOnLoad]
	public static class AstarUpdateChecker {

#if UNITY_2018_1_OR_NEWER
		static UnityWebRequest updateRequest;
#else
		static WWW updateRequest;
#endif

		static DateTime _lastUpdateCheck;
		static bool _lastUpdateCheckRead;

		static Version _latestVersion;
		static Version _latestBetaVersion;
		static string _latestVersionDescription;

		static bool hasParsedServerMessage;

		const double updateCheckRate = 1.0;

		/// 🔒 HTTPS REQUIRED
		const string updateURL = "https://www.arongranberg.com/astar/version.php";

		static readonly Dictionary<string, string> astarServerData = new Dictionary<string, string> {
			{ "URL:modifiers",      "https://www.arongranberg.com/astar/docs/modifiers.php" },
			{ "URL:astarpro",       "https://arongranberg.com/unity/a-pathfinding/astarpro/" },
			{ "URL:documentation",  "https://arongranberg.com/astar/docs/" },
			{ "URL:findoutmore",    "https://arongranberg.com/astar" },
			{ "URL:download",       "https://arongranberg.com/unity/a-pathfinding/download" },
			{ "URL:changelog",      "https://arongranberg.com/astar/docs/changelog.php" },
			{ "URL:tags",           "https://arongranberg.com/astar/docs/tags.php" },
			{ "URL:homepage",       "https://arongranberg.com/astar/" }
		};

		static AstarUpdateChecker() {
			EditorApplication.update += UpdateCheckLoop;
			EditorBase.getDocumentationURL = () => GetURL("documentation");
		}

		#region Public API

		public static DateTime lastUpdateCheck {
			get {
				if (_lastUpdateCheckRead) return _lastUpdateCheck;

				if (!DateTime.TryParse(
					EditorPrefs.GetString("AstarLastUpdateCheck", "1971-01-01"),
					System.Globalization.CultureInfo.InvariantCulture,
					System.Globalization.DateTimeStyles.None,
					out _lastUpdateCheck
				)) {
					_lastUpdateCheck = DateTime.UtcNow;
				}

				_lastUpdateCheckRead = true;
				return _lastUpdateCheck;
			}
			private set {
				_lastUpdateCheck = value;
				EditorPrefs.SetString(
					"AstarLastUpdateCheck",
					value.ToString(System.Globalization.CultureInfo.InvariantCulture)
				);
			}
		}

		public static Version latestVersion {
			get { RefreshServerMessage(); return _latestVersion ?? AstarPath.Version; }
		}

		public static Version latestBetaVersion {
			get { RefreshServerMessage(); return _latestBetaVersion ?? AstarPath.Version; }
		}

		public static string latestVersionDescription {
			get { RefreshServerMessage(); return _latestVersionDescription ?? ""; }
		}

		public static string GetURL(string tag) {
			RefreshServerMessage();
			return astarServerData.TryGetValue("URL:" + tag, out var url) ? url : "";
		}

		public static void CheckForUpdatesNow() {
			lastUpdateCheck = DateTime.UtcNow.AddDays(-5);
			EditorApplication.update -= UpdateCheckLoop;
			EditorApplication.update += UpdateCheckLoop;
		}

		#endregion

		#region Update Loop

		static void UpdateCheckLoop() {
			if (!CheckForUpdates()) {
				EditorApplication.update -= UpdateCheckLoop;
			}
		}

		static bool CheckForUpdates() {
			if (updateRequest != null) {
#if UNITY_2018_1_OR_NEWER
				if (!updateRequest.isDone) return true;

				if (updateRequest.result != UnityWebRequest.Result.Success) {
					Debug.LogWarning("A* Update check failed:\n" + updateRequest.error);
					updateRequest.Dispose();
					updateRequest = null;
					return false;
				}

				UpdateCheckCompleted(updateRequest.downloadHandler.text);
				updateRequest.Dispose();
#else
				if (!updateRequest.isDone) return true;
				UpdateCheckCompleted(updateRequest.text);
#endif
				updateRequest = null;
			}

			var offset = (Application.isPlaying && Time.time > 60) || AstarPath.active != null ? -20 : 20;
			var minutesLeft = lastUpdateCheck
				.AddDays(updateCheckRate)
				.AddMinutes(offset)
				.Subtract(DateTime.UtcNow)
				.TotalMinutes;

			if (minutesLeft < 0) {
				DownloadVersionInfo();
			}

			return minutesLeft < 10;
		}

		#endregion

		#region Networking

		static void DownloadVersionInfo() {
			var script = AstarPath.active ?? GameObject.FindObjectOfType<AstarPath>();

			bool hasGraphs = script != null && script.data?.graphs != null;

			string query =
				updateURL +
				"?v=" + AstarPath.Version +
				"&pro=0" +
				"&check=" + updateCheckRate +
				"&distr=" + AstarPath.Distribution +
				"&unitypro=" + (Application.HasProLicense() ? 1 : 0) +
				"&inscene=" + (script != null ? 1 : 0) +
				"&targetplatform=" + EditorUserBuildSettings.activeBuildTarget +
				"&devplatform=" + Application.platform +
				"&mecanim=" + (UnityEngine.Object.FindObjectOfType<Animator>() != null ? 1 : 0) +
				"&hasNavmesh=" + (hasGraphs && script.data.graphs.Any(g => g?.GetType().Name == "NavMeshGraph") ? 1 : 0) +
				"&hasPoint=" + (hasGraphs && script.data.graphs.Any(g => g?.GetType().Name == "PointGraph") ? 1 : 0) +
				"&hasGrid=" + (hasGraphs && script.data.graphs.Any(g => g?.GetType().Name == "GridGraph") ? 1 : 0) +
				"&hasLayered=" + (hasGraphs && script.data.graphs.Any(g => g?.GetType().Name == "LayerGridGraph") ? 1 : 0) +
				"&hasRecast=" + (hasGraphs && script.data.graphs.Any(g => g?.GetType().Name == "RecastGraph") ? 1 : 0) +
				"&hasCustom=" + (hasGraphs && script.data.graphs.Any(g => g != null && !g.GetType().FullName.Contains("Pathfinding.")) ? 1 : 0) +
				"&graphCount=" + (hasGraphs ? script.data.graphs.Count(g => g != null) : 0) +
				"&unityversion=" + Application.unityVersion +
				"&branch=" + AstarPath.Branch;

#if UNITY_2018_1_OR_NEWER
			updateRequest = UnityWebRequest.Get(query);
			updateRequest.SendWebRequest();
#else
			updateRequest = new WWW(query);
#endif
			lastUpdateCheck = DateTime.UtcNow;
		}

		#endregion

		#region Parsing

		static void UpdateCheckCompleted(string result) {
			EditorPrefs.SetString("AstarServerMessage", result);
			ParseServerMessage(result);
			ShowUpdateWindowIfRelevant();
		}

		static void RefreshServerMessage() {
			if (hasParsedServerMessage) return;

			var msg = EditorPrefs.GetString("AstarServerMessage", "");
			if (!string.IsNullOrEmpty(msg)) {
				ParseServerMessage(msg);
				ShowUpdateWindowIfRelevant();
			}
		}

		static void ParseServerMessage(string result) {
			if (string.IsNullOrEmpty(result)) return;

			hasParsedServerMessage = true;
			var parts = result.Split('|');

			_latestVersionDescription = parts.Length > 1 ? parts[1] : "";

			if (parts.Length > 4) {
				for (int i = 4; i + 1 < parts.Length; i += 2) {
					astarServerData[parts[i]] = parts[i + 1];
				}
			}

			if (astarServerData.TryGetValue("VERSION:branch", out var v))
				Version.TryParse(v, out _latestVersion);

			if (astarServerData.TryGetValue("VERSION:beta", out var b))
				Version.TryParse(b, out _latestBetaVersion);
		}

		#endregion

		static void ShowUpdateWindowIfRelevant() {
#if !ASTAR_ATAVISM
			var skip = new Version(EditorPrefs.GetString("AstarSkipUpToVersion", AstarPath.Version.ToString()));
			if (AstarPathEditor.FullyDefinedVersion(latestVersion) >
				AstarPathEditor.FullyDefinedVersion(skip)) {

				EditorPrefs.DeleteKey("AstarSkipUpToVersion");
				AstarUpdateWindow.Init(latestVersion, latestVersionDescription);
			}
#endif
		}
	}
}
