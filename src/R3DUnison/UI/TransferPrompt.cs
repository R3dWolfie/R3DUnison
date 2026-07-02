using R3DUnison.Session;
using UnityEngine;

namespace R3DUnison.UI
{
    /// <summary>
    /// Top-center prompt when the host starts a level you don't have:
    /// offer with size → DOWNLOAD/IGNORE → live progress while it streams in.
    /// </summary>
    public class TransferPrompt : MonoBehaviour
    {
        private void OnGUI()
        {
            bool hasOffer = LevelTransfer.PendingOffer != null;
            bool downloading = LevelTransfer.DownloadProgress >= 0f;
            string status = LevelTransfer.PeerStatus;
            if (!hasOffer && !downloading && status == null) return;

            float scale = Mathf.Max(1f, Screen.height / 1080f);
            var previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            UnisonTheme.Ensure();

            float width = 560f;
            float virtualWidth = Screen.width / scale;
            var area = new Rect((virtualWidth - width) / 2f, 40f, width, 150f);
            GUILayout.BeginArea(area, UnisonTheme.Overlay);
            GUILayout.Label("R3D UNISON", UnisonTheme.OverlayHead);
            GUILayout.Space(4);
            if (hasOffer)
            {
                var offer = LevelTransfer.PendingOffer;
                GUILayout.Label($"Host started '{LevelTransfer.PendingDisplay}' — you don't have it.", UnisonTheme.Label);
                GUILayout.Space(8);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button($"DOWNLOAD ({offer.Size / 1048576f:0.0} MB)", UnisonTheme.ButtonPrimary, GUILayout.Width(230)))
                {
                    LevelTransfer.Accept();
                }
                GUILayout.Space(8);
                if (GUILayout.Button("IGNORE", UnisonTheme.Button, GUILayout.Width(110)))
                {
                    LevelTransfer.Decline();
                }
                GUILayout.EndHorizontal();
            }
            else if (downloading)
            {
                GUILayout.Label($"Downloading '{LevelTransfer.PendingDisplay}' — {LevelTransfer.DownloadProgress:P0}", UnisonTheme.LevelText);
            }
            else
            {
                GUILayout.Label(status, UnisonTheme.Label);
            }
            GUILayout.EndArea();

            GUI.matrix = previousMatrix;
        }
    }
}
