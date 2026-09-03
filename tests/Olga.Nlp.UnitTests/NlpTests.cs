using Olga.Nlp.Infrastructure;

namespace Olga.Nlp.UnitTests;
public sealed class NlpTests
{
    [Fact] public void Normalization_is_deterministic_and_masks_pii() { var n = new TextNormalizer(new PiiChecker()); var a = n.Normalize("  Need  cold-chain\tstorage. Email a@b.com "); Assert.Equal("Need cold-chain storage. Email [REDACTED]", a.Value); Assert.True(a.ContainsPii); Assert.Equal(a.Hash, n.Normalize("Need cold-chain storage. Email a@b.com").Hash); }
    [Fact] public async Task Fake_embeddings_are_repeatable() { var p = new FakeEmbeddingProvider(); Assert.Equal(await p.EmbedAsync("cold chain", default), await p.EmbedAsync("cold chain", default)); }
    [Fact] public void Reciprocal_score_is_bounded() { var s = new ReciprocalScorer().Score(new float[] { 1, 0 }, new float[] { 1, 0 }, new float[] { 0, 1 }, new float[] { 1, 0 }); Assert.InRange(s.Reciprocal, 0, 1); }
}
