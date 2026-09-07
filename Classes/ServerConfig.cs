using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mocha2023.Classes
{
    public class ServerConfig
    {
        public static object Bracket => new List<object>();
        public static string BaseURL = "https://playmocha.xyz";
        public static int GameVersion = 20230406;

        public const string PhotonRealtimeAppId =
            "94e5bc5c-d19b-4651-b3c0-e0f7758c17e1";

        public const string PhotonVoiceAppId =
            "bfa91264-1580-4e63-b355-77404b95e33e";

        public const string PhotonRegion = "us";
        public const string PhotonAppVersion = "20230406_prod";
        public static readonly string PhotonCustomAuthSecret =
            Program.LoadLocalSetting("PHOTON_CUSTOM_AUTH_SECRET");

        public static bool PhotonEnabled =>
            !string.IsNullOrWhiteSpace(PhotonRealtimeAppId);

        public static readonly Version MinModVersion = new Version(1, 0, 8);

        // Quest has no PC patch to install, so the MinModVersion gate
        // cannot apply to it. Set to false to re-enforce the gate on
        // every platform.
        public static readonly bool ExemptQuestFromModGate = true;
    }
}
