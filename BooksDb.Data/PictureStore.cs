//using System.IO;

namespace BooksDb.Data;

public sealed class PictureStore
{
    public string BaseDir { get; }

    public PictureStore()
    {
        BaseDir = Path.GetDirectoryName(Database.DefaultPath()) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(Path.Combine(BaseDir, "pictures"));
    }

    public string AbsolutePath(string relative) => Path.Combine(BaseDir, relative);

    // Move an image out of active use into the orphaned-pictures folder.
    public void Archive(string absolutePath)
    {
        var orphanDir = Path.Combine(BaseDir, "pictures_orphaned");
        Directory.CreateDirectory(orphanDir);
        var dest = Path.Combine(orphanDir, Path.GetFileName(absolutePath));
        File.Move(absolutePath, dest, overwrite: true);   // GUID names won't collide
    }
}