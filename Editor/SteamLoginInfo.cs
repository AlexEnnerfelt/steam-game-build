
using UnityEngine;

namespace Carnage.BuildEditor {
	public class SteamLoginInfo : ScriptableObject {
		public const string k_Path = "Assets/Editor/SteamLoginInfo.asset";
		public static SteamLoginInfo current = null;
		public string username;
		public string password;
	}
}