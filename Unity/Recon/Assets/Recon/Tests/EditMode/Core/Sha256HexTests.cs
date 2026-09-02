using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using Recon.Scenes;

namespace Recon.Tests.Core
{
    /// <summary>
    /// Custody, client side. The asset route returns <c>X-Asset-SHA256</c> so Unity can prove the
    /// bytes it received are the bytes custody recorded; this is the hash it compares against.
    /// Checked against the published NIST vectors rather than against itself, because a hash
    /// function that agrees only with its own output would pass any round-trip test while still
    /// disagreeing with the server.
    /// </summary>
    public class Sha256HexTests
    {
        const string EmptyVector = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        const string AbcVector = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

        [Test]
        public void ComputeHex_EmptyInput_MatchesTheKnownVector()
        {
            Assert.That(Sha256Hex.ComputeHex(new byte[0]), Is.EqualTo(EmptyVector));
        }

        [Test]
        public void ComputeHex_Abc_MatchesTheKnownVector()
        {
            Assert.That(Sha256Hex.ComputeHex(Encoding.ASCII.GetBytes("abc")), Is.EqualTo(AbcVector));
        }

        [Test]
        public void ComputeHex_IsLowercase_LikeTheServersHex()
        {
            // web/src/lib/custody.ts and the Asset.sha256 column store lowercase hex. An
            // uppercase comparison would fail every verification for a reason nobody would guess.
            var hex = Sha256Hex.ComputeHex(Encoding.ASCII.GetBytes("abc"));

            Assert.That(hex, Is.EqualTo(hex.ToLowerInvariant()));
            Assert.That(hex, Has.Length.EqualTo(64));
            Assert.That(hex, Does.Match("^[0-9a-f]{64}$"));
        }

        [Test]
        public void ComputeHex_Stream_AgreesWithTheByteArrayOverload()
        {
            var bytes = Encoding.ASCII.GetBytes("abc");
            using (var stream = new MemoryStream(bytes))
            {
                Assert.That(Sha256Hex.ComputeHex(stream), Is.EqualTo(AbcVector));
            }
        }

        [Test]
        public void ComputeHex_Stream_HandlesMoreThanOneBuffer()
        {
            // A 45 MB .ply is read in chunks; a bug in the loop would only show above the buffer
            // size, which is exactly the case that never gets exercised by a three-byte test.
            var big = new byte[1024 * 1024 + 7];
            for (int i = 0; i < big.Length; i++) big[i] = (byte)(i * 31 % 251);

            string fromArray = Sha256Hex.ComputeHex(big);
            using (var stream = new MemoryStream(big))
            {
                Assert.That(Sha256Hex.ComputeHex(stream), Is.EqualTo(fromArray));
            }
        }

        [Test]
        public void ComputeHex_ReadsFromTheCurrentStreamPosition_Documented()
        {
            // Callers hand over a freshly opened FileStream. If a stream is already partly read
            // the hash covers only the remainder, which is worth being explicit about.
            var bytes = Encoding.ASCII.GetBytes("Xabc");
            using (var stream = new MemoryStream(bytes))
            {
                stream.Position = 1;
                Assert.That(Sha256Hex.ComputeHex(stream), Is.EqualTo(AbcVector));
            }
        }

        [Test]
        public void ComputeFileHex_HashesAFileOnDisk()
        {
            string path = Path.Combine(Path.GetTempPath(), "recon-sha-" + Guid.NewGuid().ToString("N") + ".bin");
            try
            {
                File.WriteAllBytes(path, Encoding.ASCII.GetBytes("abc"));
                Assert.That(Sha256Hex.ComputeFileHex(path), Is.EqualTo(AbcVector));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void Matches_IsCaseInsensitiveAndNullSafe()
        {
            // The header value is whatever the server wrote; compare on value, not on casing.
            Assert.That(Sha256Hex.Matches(AbcVector, AbcVector.ToUpperInvariant()), Is.True);
            Assert.That(Sha256Hex.Matches(AbcVector, EmptyVector), Is.False);
            Assert.That(Sha256Hex.Matches(AbcVector, null), Is.False);
            Assert.That(Sha256Hex.Matches(null, AbcVector), Is.False);
            Assert.That(Sha256Hex.Matches(AbcVector, "  " + AbcVector + " "), Is.True);
        }

        [Test]
        public void Matches_RefusesThePlaceholderHash()
        {
            // docs/samples/metadata.example.json ships all zeros. Treating that as "matches"
            // would turn the custody check into a no-op on any scene that never got hashed.
            const string zeros = "0000000000000000000000000000000000000000000000000000000000000000";

            Assert.That(Sha256Hex.Matches(zeros, zeros), Is.False);
            Assert.That(Sha256Hex.IsUsableHash(zeros), Is.False);
            Assert.That(Sha256Hex.IsUsableHash(AbcVector), Is.True);
            Assert.That(Sha256Hex.IsUsableHash("abc"), Is.False, "wrong length");
        }

        [Test]
        public void ComputeHex_NullInput_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => Sha256Hex.ComputeHex((byte[])null));
            Assert.Throws<ArgumentNullException>(() => Sha256Hex.ComputeHex((Stream)null));
        }
    }
}
