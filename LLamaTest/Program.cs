using System;
using LLama;
using LLama.Common;
class P {
    static void Main() {
        try {
            var m = LLamaWeights.LoadFromFile(new ModelParams("C:\\Users\\crcow\\AppData\\Local\\DoubleDeckCancellationHearts\\models\\gemma-4-E4B-it-Q4_K_M.gguf") { GpuLayerCount = 10 });
            Console.WriteLine("SUCCESS");
        } catch (Exception e) {
            Console.WriteLine("EX_START " + e.ToString() + " EX_END");
        }
    }
}
