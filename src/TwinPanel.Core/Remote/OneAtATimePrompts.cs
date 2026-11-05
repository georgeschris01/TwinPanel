// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
namespace TwinPanel.Core.Remote;

/// <summary>
/// Plusieurs serveurs se connectent en même temps (démarrage) : leurs questions (empreinte, mot de passe,
/// phrase secrète, mot de passe maître) arrivent quand même une à la fois, jamais deux fenêtres ensemble.
/// Le mot de passe maître n'est demandé qu'une fois : si le coffre a été déverrouillé pendant l'attente, on ne redemande pas.
/// </summary>
public sealed class OneAtATimePrompts(IConnectionPrompts inner) : IConnectionPrompts
{
    private readonly SemaphoreSlim _turn = new(1, 1);

    private async Task<T> InTurnAsync<T>(Func<Task<T>> ask)
    {
        await _turn.WaitAsync().ConfigureAwait(false);
        try { return await ask().ConfigureAwait(false); }
        finally { _turn.Release(); }
    }

    public Task<HostKeyDecision> ConfirmHostKeyAsync(HostKeyPrompt prompt) => InTurnAsync(() => inner.ConfirmHostKeyAsync(prompt));
    public Task<CertificateDecision> ConfirmCertificateAsync(CertificatePrompt prompt) => InTurnAsync(() => inner.ConfirmCertificateAsync(prompt));
    public Task<SecretAnswer?> AskSecretAsync(SecretRequest request) => InTurnAsync(() => inner.AskSecretAsync(request));
    public Task<bool> UnlockVaultAsync(SecretVault vault) => InTurnAsync(() => vault.IsUnlocked ? Task.FromResult(true) : inner.UnlockVaultAsync(vault));
    public Task<IReadOnlyList<string>?> AnswerInteractiveAsync(string title, string instruction, IReadOnlyList<InteractivePrompt> prompts) =>
        InTurnAsync(() => inner.AnswerInteractiveAsync(title, instruction, prompts));
}
