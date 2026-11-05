// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Diagnostics;
using TwinPanel.Core.Settings;
using TwinPanel.Core.Tools;

namespace TwinPanel.App.Services;

/// <summary>
/// Lance l'éditeur (F4) et la visionneuse externe (F3) choisis dans les réglages.
/// Si le programme choisi n'existe plus, on prévient et on se replie sur le Bloc-notes : jamais d'erreur brute.
/// </summary>
public sealed class ToolLauncher(IDialogService dialogs, Func<AppSettings> settings)
{
    /// <summary>Éditeur par défaut : Bloc-notes sous Windows, ouverture par le système ailleurs.</summary>
    public static ExternalTool DefaultEditor { get; } = OperatingSystem.IsWindows()
        ? ExternalTools.WindowsNotepad
        : new ExternalTool(L.T("Éditeur du système"), "xdg-open", "\"%1\"");

    public ExternalTool CurrentEditor
    {
        get
        {
            var s = settings();
            return string.IsNullOrWhiteSpace(s.EditorPath)
                ? DefaultEditor
                : new ExternalTool(string.IsNullOrWhiteSpace(s.EditorName) ? Path.GetFileNameWithoutExtension(s.EditorPath) : s.EditorName,
                                   s.EditorPath, s.EditorArguments);
        }
    }

    /// <summary>Éditeur à utiliser pour ce fichier : règle par extension (Options → Éditeurs), sinon l'éditeur F4 général.</summary>
    public ExternalTool EditorFor(string filePath)
    {
        var rule = settings().EditorRules.FirstOrDefault(r => r.Matches(filePath));
        return rule is null ? CurrentEditor
            : new ExternalTool(rule.Name.Length > 0 ? rule.Name : Path.GetFileNameWithoutExtension(rule.Path), rule.Path, rule.Arguments);
    }

    public async Task EditAsync(string filePath, int line = 1)
    {
        var tool = EditorFor(filePath);
        if (tool != CurrentEditor && !ExternalTools.IsInternal(tool.Path) && !ExternalTools.IsAvailable(tool.Path))
        {
            await dialogs.ShowErrorAsync(L.T("Éditeur introuvable"),
                L.F("L'éditeur prévu pour les fichiers .{0} est introuvable :{1}{2}", Path.GetExtension(filePath).TrimStart('.'), Environment.NewLine, tool.Path) +
                L.F("{0}{1}Le fichier s'ouvre avec l'éditeur général. (Options → Éditeurs pour corriger.)", Environment.NewLine, Environment.NewLine));
            tool = CurrentEditor;
        }
        if (!ExternalTools.IsAvailable(tool.Path) && tool != DefaultEditor)
        {
            bool reset = await dialogs.ConfirmAsync(L.T("Éditeur introuvable"),
                L.F("L'éditeur « {0} » est introuvable :{1}{2}{3}{4}", tool.Name, Environment.NewLine, tool.Path, Environment.NewLine, Environment.NewLine) +
                L.F("Le fichier va s'ouvrir avec : {0}.{1}Revenir à cet éditeur par défaut pour la suite ?", DefaultEditor.Name, Environment.NewLine));
            if (reset)
            {
                var s = settings();
                s.EditorName = string.Empty;
                s.EditorPath = string.Empty;
                s.EditorArguments = "\"%1\"";
            }
            tool = DefaultEditor;
        }
        if (ExternalTools.IsInternal(tool.Path))
        {
            await dialogs.ShowEditorAsync(filePath, line);
            return;
        }
        await StartAsync(tool, filePath, line);
    }

    /// <summary>F3 avec visionneuse externe. Retourne false s'il faut utiliser la visionneuse intégrée.</summary>
    public async Task<bool> TryExternalViewAsync(string filePath)
    {
        var s = settings();
        if (string.IsNullOrWhiteSpace(s.ViewerPath)) return false;
        if (!ExternalTools.IsAvailable(s.ViewerPath))
        {
            await dialogs.ShowErrorAsync(L.T("Visionneuse introuvable"),
                L.F("La visionneuse externe est introuvable :{0}{1}{2}La visionneuse intégrée est utilisée à la place.", Environment.NewLine, s.ViewerPath, Environment.NewLine));
            return false;
        }
        await StartAsync(new ExternalTool(L.T("Visionneuse"), s.ViewerPath, s.ViewerArguments), filePath, 1);
        return true;
    }

    private async Task StartAsync(ExternalTool tool, string filePath, int line)
    {
        try
        {
            Process.Start(ExternalTools.BuildStartInfo(tool, filePath, line))?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            await dialogs.ShowErrorAsync(L.T("Impossible de lancer le programme"), $"{tool.Name} : {ex.Message}");
        }
    }
}
