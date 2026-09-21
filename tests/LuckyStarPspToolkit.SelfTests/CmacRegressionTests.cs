using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace LuckyStarPspToolkit.SelfTests;

/// <summary>Runtime regressions for bounded-memory authentication; expected tags were produced by Python cryptography, not this C# implementation.</summary>
internal static partial class Program
{
    /// <summary>Checks independently generated tags around AES-block and 64-KiB chunk boundaries and verifies input immutability.</summary>
    private static void TestCmacIndependentVectors()
    {
        (int Length, string Tag)[] vectors =
        [
            (0, "97dd6e5a882cbd564c39ae7d1c5a31aa"),
            (1, "a37b1cae78c89684424eb7025d92e88b"),
            (2, "8b386aef1cdf1b14012709b8559b16f8"),
            (3, "ee8e8c280a7b5c217f9db1085c523cad"),
            (4, "36e6fb7ef9731141751a05cdb5600cc0"),
            (5, "11b629121c60474ffee7676502904a95"),
            (6, "d72774bd7c8f6d4fa900ac73e47d702e"),
            (7, "139d8cdb84b40e70bc1af7ab520b9927"),
            (8, "ef9ba86bde0ff44267f8ad2cf443c7ab"),
            (9, "17c7a834ed6a913cbd0c76fd877b0714"),
            (10, "5d26511225701a4b18571acd55dcbf47"),
            (11, "74dbf5d86471ecfc09df6e6cbfacb552"),
            (12, "7333610d62050e108b132d12402289ab"),
            (13, "9fb5fb5603ec21096c46aace1fa40c12"),
            (14, "aa9b66441d771fbc0e7f4c0b4cfee57e"),
            (15, "9718bc98a845635d535b320421c90769"),
            (16, "22420c53f644d911f4a612a3f43b88fa"),
            (17, "0d30154ae6d6e4353bad543400c27e78"),
            (18, "6107cd435c14ec4b93a2feb246d647d0"),
            (19, "27947a2c20d893c0859621da65d0de53"),
            (20, "08c69c9837cd2eb243f6a5f1febb0d1a"),
            (21, "c1c0ab02222040078edb1fc9225b8f4b"),
            (22, "a0a4e0f36d359e3c4985194772d4793a"),
            (23, "857ceb3e4ceb13cad962eec7abf5832f"),
            (24, "83855fe9f74c58e2b3d8216ee3824a27"),
            (25, "0e7eba1425ca629384f501bbb87f026d"),
            (26, "2e68701923ece9de32dcda699d678594"),
            (27, "6ec611cfea455aeaca848d9dc8e25b6d"),
            (28, "e485bac1f9c74d1185a03d0cde057bf4"),
            (29, "ca42de9934e9a8c7be6c12b981768368"),
            (30, "13813efcf4c4c20091fd7cb6882fd450"),
            (31, "315d0eb10e3a2b976546e2eabcc17187"),
            (32, "97939d9bd4bf46b276a1896152a9d0e0"),
            (33, "c6ec263305a70d45991ee48628a76cd3"),
            (34, "04fa077ad2b4c8512b6a47c19c49c2f5"),
            (35, "e107180f5c9837d994f7a8fcc7fe0d45"),
            (36, "155718d47f3040480cad8575b96091c4"),
            (37, "932426863ccea1511fb0df52a29bcd7e"),
            (38, "482373b814269f4a74ba4dff01227c15"),
            (39, "7232169b13bacfd4f054c766044581ef"),
            (40, "e11236efb5c802f994a84fcb3e816e0d"),
            (41, "804296b2f8eee45963f0247e3da4e9c5"),
            (42, "33cd9c5ac228938ed91e50f953069643"),
            (43, "933b6da3cfb85ed192054d40a0d5ee17"),
            (44, "fe05c3f7d04855f67c4aa1797c7c8070"),
            (45, "97b840a358da717870b910bff31f3824"),
            (46, "d4b1ef6537a07448e76af614f12cb191"),
            (47, "407c2a7742e91d6121aecd7046db687e"),
            (48, "8b9dff6b1d732328eae037b2753a965e"),
            (49, "1b094abfd44cd5899d08a867ed91511a"),
            (50, "6b329640067848cb705e07ce95b67fb3"),
            (51, "b2169cd4d7b79ba685954c5223f53a55"),
            (52, "4fa0e14afa07a6c86fdaecfd19e122ae"),
            (53, "691dd63aadbfe22a867f70dac3c2a448"),
            (54, "7de794b312103074f55ee9f911f66e73"),
            (55, "37e82704faf096b8777d07962ba84bec"),
            (56, "37548e7c0f9778facf0e1245b278913b"),
            (57, "f74af3b9cc40e00561bd08d595bc733b"),
            (58, "06fd750b49bcba1d4f3d3c640b0f686d"),
            (59, "2a182c8d441c46ed760740c5deef15d3"),
            (60, "531dfc36c2eeb2d27c2eff7202d56a9e"),
            (61, "28e25ea3038b5ec0d8304427d5169e49"),
            (62, "87b877bcb93d690004aa9dd091115592"),
            (63, "db5e347c11d8865b9b0ec9239c4d9be8"),
            (64, "e0aaa178c8b8680323097925ebafc3a9"),
            (127, "bd5bb7ae0593ec0bac69aea7f9fae6d4"),
            (128, "f8428bad5f447985e3ce645a98472789"),
            (129, "4988a8a588fda1d9ab105040815ef3ae"),
            (4095, "5397a7ace73d6cb845fc0287ce8308af"),
            (4096, "55587581e19d8bd9ddb99d76dc96e2b5"),
            (4097, "ae697c9e690a34783dc1f57df6529d74"),
            (65519, "385f96e09c8e5ebf62b78983b49ce716"),
            (65520, "ce7ad171363c3dfa33502f91a5210fa6"),
            (65521, "f0e3e68b8744712864fed82f1781c8bb"),
            (65535, "2572dd22bec6cedcba2ac97c836050d2"),
            (65536, "4e0c4613c08ccc5e7f34a8cb1d9969a2"),
            (65537, "8dbb186eca16a9b788c9bb713d831a3a"),
            (131071, "9b400e6bd005dfcc3c15a8c3264988a1"),
            (131072, "a7bfbe5c38342050dd44b05e877fb79d"),
            (131073, "90088e61346573957d7291ef9632f7e8"),
            (1048576, "315fe2a71bbbd1c6a1c139463972d12d"),
        ];
        byte[] key = Enumerable.Range(0, 16).Select(static x => (byte)x).ToArray();
        foreach ((int length, string tag) in vectors)
        {
            byte[] data = new byte[length];
            for (int i = 0; i < data.Length; i++) data[i] = (byte)(i * 31 + 17);
            byte[] hash = SHA256.HashData(data);
            string actual = Convert.ToHexString(CryptoUtilities.Aes128Cmac(data, key)).ToLowerInvariant();
            Require(actual == tag, $"CMAC mismatch at length {length}");
            Require(hash.SequenceEqual(SHA256.HashData(data)), "CMAC changed its input");
        }
        RequireThrows<ToolkitException>(() => CryptoUtilities.Aes128Cmac([], new byte[15]));
        Console.WriteLine($"Independent CMAC vectors: {vectors.Length}");
    }

    /// <summary>Warms the pool, then checks that authenticating four MiB does not allocate a message-sized managed copy.</summary>
    private static void TestCmacAllocation()
    {
        byte[] data = new byte[4 * 1024 * 1024];
        byte[] key = new byte[16];
        _ = CryptoUtilities.Aes128Cmac(data, key);
        long start = GC.GetAllocatedBytesForCurrentThread();
        long clock = Stopwatch.GetTimestamp();
        byte[] tag = CryptoUtilities.Aes128Cmac(data, key);
        double elapsed = Stopwatch.GetElapsedTime(clock).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Require(tag.Length == 16, "Wrong CMAC output length");
        Require(allocated < 256 * 1024, $"CMAC allocated {allocated} managed bytes after pool warmup");
        Console.WriteLine(JsonSerializer.Serialize(new { check = "cmac-allocation", inputBytes = data.Length,
            managedAllocatedBytes = allocated, elapsedMilliseconds = elapsed, poolWarm = true }));
    }
}
