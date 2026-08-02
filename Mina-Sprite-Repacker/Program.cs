namespace Mina_Sprite_Repacker
{
    static class Program
    {
        static void Main(string[] args)
        {
            bool repackMode = false;
            bool getNames = false;
            string repackFilePath = "";

            if (args.Length == 0) {
                Console.WriteLine("No arguments given: you can -extract, -repack, or -repack \"filepath\"");
                return;
            }
            if (args[0] == "-extract" || args[0] == "-e") {
                if (args.Length >= 2) {
                    if (args[1] == "-names" || args[1] == "-n") {
                        getNames = true;
                    }
                }

                Console.WriteLine("Extracting all sprites...");
                Extract.ExtractAllSprites(getNames);
                Console.WriteLine($"Finished extracting");
            }
            else if (args[0] == "-repack" || args[0] == "-r") {
                repackMode = true;
            }
            else {
                Console.WriteLine($"Unknown argument '{args[0]}' given: you can -extract, -repack, or -repack \"filepath\"");
                return;
            }

            if (repackMode) {
                if (args.Length < 2) {
                    Console.WriteLine("Repacking all sprites...");
                    Repack.RepackAllSprites();
                }
                else {
                    repackFilePath = args[1];
                    Console.WriteLine($"Repacking {repackFilePath}...");
                    Repack.RepackSingleSprite(repackFilePath);
                }

                Console.WriteLine($"Finished repacking");
            }

            return;
        }
    }
}