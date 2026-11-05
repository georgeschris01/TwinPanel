// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Remote;
using Xunit;

namespace TwinPanel.Core.Tests;

public class OneAtATimePromptsTests
{
    /// <summary>Compte les questions ouvertes en même temps.</summary>
    private sealed class CountingPrompts : IConnectionPrompts
    {
        private int _open;
        public int MaxOpen;
        public int Asked;

        private async Task<T> Open<T>(T answer)
        {
            int now = Interlocked.Increment(ref _open);
            InterlockedMax(now);
            Interlocked.Increment(ref Asked);
            await Task.Delay(30);
            Interlocked.Decrement(ref _open);
            return answer;
        }

        private void InterlockedMax(int value)
        {
            int seen;
            while (value > (seen = Volatile.Read(ref MaxOpen)) && Interlocked.CompareExchange(ref MaxOpen, value, seen) != seen) { }
        }

        public Task<HostKeyDecision> ConfirmHostKeyAsync(HostKeyPrompt prompt) => Open(default(HostKeyDecision));
        public Task<CertificateDecision> ConfirmCertificateAsync(CertificatePrompt prompt) => Open(default(CertificateDecision));
        public Task<SecretAnswer?> AskSecretAsync(SecretRequest request) => Open<SecretAnswer?>(null);
        public Task<bool> UnlockVaultAsync(SecretVault vault) => Open(true);
        public Task<IReadOnlyList<string>?> AnswerInteractiveAsync(string title, string instruction, IReadOnlyList<InteractivePrompt> prompts) =>
            Open<IReadOnlyList<string>?>(null);
    }

    [Fact]
    public async Task Questions_of_servers_connecting_together_come_one_at_a_time()
    {
        var inner = new CountingPrompts();
        var prompts = new OneAtATimePrompts(inner);
        // Trois serveurs qui demandent en même temps (le contenu de la question n'est pas lu par le faux).
        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(() => prompts.AskSecretAsync(null!))));
        Assert.Equal(3, inner.Asked);
        Assert.Equal(1, inner.MaxOpen);
    }
}
