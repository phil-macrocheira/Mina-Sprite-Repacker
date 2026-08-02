namespace Mina_Sprite_Repacker
{
    static class Program
    {
        static void Main(string[] args)
        {
            string currentDirectory = Path.GetDirectoryName(Environment.CurrentDirectory);
            bool repackMode = false;
            string repackFilePath = "";

            if (args.Length == 0) {
                Console.WriteLine("No arguments given: you can -extract, -repack, or -repack \"filepath\"");
                return;
            }
            if (args[0] == "-extract" || args[0] == "extract" || args[0] == "-e") {
                Console.WriteLine("Extracting all sprites...");
                Extract.ExtractAllSprites(currentDirectory);
            }
            else if (args[0] == "-repack" || args[0] == "repack" || args[0] == "-r") {
                repackMode = true;
            }
            else {
                Console.WriteLine($"Unknown argument '{args[0]}' given: you can -extract, -repack, or -repack \"filepath\"");
                return;
            }

            if (repackMode) {
                if (args.Length < 2) {
                    Console.WriteLine("Repacking all sprites...");
                    Repack.RepackAllSprites(currentDirectory);
                }
                else {
                    repackFilePath = args[1];
                    Console.WriteLine($"Repacking {repackFilePath}...");
                    Repack.RepackSingleSprite(currentDirectory, repackFilePath);
                }
            }

            return;
        }
    }
}