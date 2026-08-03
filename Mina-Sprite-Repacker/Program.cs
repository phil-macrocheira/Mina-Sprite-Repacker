namespace Mina_Sprite_Repacker
{
    static class Program
    {
        static void Main(string[] args)
        {
            bool repackMode = false;
            bool getNames = true;
            string repackFilePath = "";

            if (args.Length == 0) {
                Console.WriteLine("No arguments given: you can -extract or -repack");
                return;
            }
            if (args[0] == "-extract" || args[0] == "-e") {
                if (args.Length >= 2) {
                    if (args[1] == "-nonames" || args[1] == "-n") {
                        getNames = false;
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
                Console.WriteLine($"Unknown argument '{args[0]}' given: you can -extract or -repack");
                return;
            }

            if (repackMode) {
                if (args.Length < 2) {
                    Console.WriteLine("Repacking sprites...");
                    Repack.RepackAllSprites();
                }

                Console.WriteLine($"Finished repacking");
            }

            return;
        }
    }
}