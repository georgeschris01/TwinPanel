// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Text;

namespace TwinPanel.Core.Terminal;

/// <summary>
/// Lecture d'une connexion qui peut envoyer plus vite que l'écran ne suit (un serveur par le réseau).
/// Tout ce qui attend reste en mémoire : sans limite, la mémoire grossit et Ctrl+C semble sans effet,
/// car le terminal déroule encore le texte reçu avant l'arrêt. Au-delà de la limite, le milieu est sauté :
/// la fin du texte reste, de quoi remplir l'historique.
/// </summary>
public static class TerminalBacklog
{
    public const long Limit = 4 * 1024 * 1024;
    public const long Tail = 1024 * 1024;

    /// <summary>Annule une suite en cours, remet le style à zéro et signale que du texte manque.</summary>
    private static byte[] Marker() =>
        Encoding.UTF8.GetBytes("\u0018\r\n\u001b[0;2m" + L.T("… texte sauté : il arrivait trop vite pour l'affichage …") + "\u001b[0m\r\n");

    /// <summary>Lit jusqu'à la fin de la connexion. <paramref name="available"/> donne ce qui attend d'être lu.</summary>
    public static void Pump(Func<byte[], int, int, int> read, Func<long> available, Action<byte[], int> deliver, long limit = Limit, long tail = Tail)
    {
        var buffer = new byte[16 * 1024];
        while (true)
        {
            int count = read(buffer, 0, buffer.Length);
            if (count <= 0) return;
            deliver(buffer, count);

            long waiting = available();
            if (waiting <= limit) continue;

            for (long skip = waiting - tail; skip > 0;)
            {
                count = read(buffer, 0, (int)Math.Min(buffer.Length, skip));
                if (count <= 0) return;
                skip -= count;
            }
            count = read(buffer, 0, buffer.Length);
            if (count <= 0) return;

            // On reprend au début d'une ligne : pas de caractère ni de commande coupés en deux.
            int start = Array.IndexOf(buffer, (byte)'\n', 0, count) + 1;
            var marker = Marker();
            deliver(marker, marker.Length);
            if (count > start)
            {
                Buffer.BlockCopy(buffer, start, buffer, 0, count - start);
                deliver(buffer, count - start);
            }
        }
    }
}
