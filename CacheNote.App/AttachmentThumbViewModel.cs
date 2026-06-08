using Microsoft.UI.Xaml.Media.Imaging;

namespace CacheNote_App;

/// <summary>A thumbnail for an attached image in the note editor.</summary>
public sealed class AttachmentThumbViewModel
{
    public AttachmentThumbViewModel(long id, string absolutePath)
    {
        Id = id;
        var bmp = new BitmapImage { DecodePixelWidth = 220 };
        bmp.UriSource = new Uri(absolutePath);
        Image = bmp;
    }

    public long Id { get; }
    public BitmapImage Image { get; }
}
