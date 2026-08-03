namespace Mina_Sprite_Repacker
{
    public static class Constants
    {
        public static string currentDirectory = Environment.CurrentDirectory;
        public static string spritesFolderName = "_my_sprites";
        public static string spritesRoot = Path.Combine(currentDirectory, spritesFolderName);
        public static string globalPaletteFilename = "global.pal.yc";
        public static string globalPalettePath = $"\"palettes/{globalPaletteFilename}\"";
        public static string globalPalettePathLocal = "Mina_Sprite_Repacker.Data.global.pal.yc";
    }
}
