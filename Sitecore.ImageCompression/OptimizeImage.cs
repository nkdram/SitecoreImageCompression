using Sitecore.Pipelines;
using System.IO;
using Sitecore.Resources.Media;
using System.Collections.Generic;
using System.Linq;
using System;
using Sitecore.Pipelines.Upload;
using Sitecore.Diagnostics;
using System.Web;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Sitecore.Data.Items;
//using TinifyAPI;
using System.Threading.Tasks;
using Sitecore.ImageCompression.Helper;

namespace Sitecore.ImageCompression
{
    public class OptimizeImage : UploadProcessor
    {
        private static readonly SynchronizedCollection<string> _inProcess = new SynchronizedCollection<string>();

        public void Process(UploadArgs args)
        {
            Assert.ArgumentNotNull((object)args, "args");

            if (_inProcess.Contains(args.Folder))
            {
                return;
            }
            _inProcess.Add(args.Folder);

            if (args.Destination == UploadDestination.File)
                return;
            var cnt = 0;
            foreach (string index in args.Files)
            {
                HttpPostedFile file = args.Files[index];
                try
                {
                    if (!string.IsNullOrEmpty(file.FileName))
                    {
                        if ((long)file.ContentLength > AllowedImageSize)
                        {
                            byte[] fileContents = new byte[file.ContentLength];
                            var fileStream = file.InputStream;
                            fileStream.Read(fileContents, 0, file.ContentLength);
                            System.Drawing.Image image = System.Drawing.Image.FromStream(new System.IO.MemoryStream(fileContents));
                            var imageFormat = GetContentType(fileContents);
                            Stream stream = new MemoryStream();
                            using (var resized = ImageUtilities.ResizeImage(image, image.Width, image.Height))
                            {
                                stream = ImageUtilities.SaveJpeg(stream, resized, 60);
                            }                          
                            var fileDetails = file.FileName.Split('.');
                            string newMediaItemName = string.Format("{0}_compressed", fileDetails[0]);
                            var id = new Data.ID(args.Folder.ToString());
                            var db = Sitecore.Data.Database.GetDatabase("master");
                            var folderItem = db.GetItem(id);
                            CreateMediaItem(stream, newMediaItemName, folderItem.Paths.Path, "." + fileDetails[1], "master");
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    args.ErrorText = string.Format("The image {0} can not be compressed.", file.FileName);
                    Log.Error(args.ErrorText, ex);
                }
                finally
                {
                    cnt++;
                }
            }
            _inProcess.Remove(args.Folder);
        }

        public static byte[] ReadFully(Stream input)
        {
            byte[] buffer = new byte[16 * 1024];
            using (MemoryStream ms = new MemoryStream())
            {
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ms.Write(buffer, 0, read);
                }
                return ms.ToArray();
            }
        }

        public MediaItem CreateMediaItem(System.IO.Stream image, string mediaItemName, string sitecorePath, string extension, string databaseName)
        {
            var options = new Sitecore.Resources.Media.MediaCreatorOptions
            {
                AlternateText = mediaItemName,
                FileBased = false,
                IncludeExtensionInItemName = false,
                Versioned = false,
                Destination = sitecorePath + "/" + mediaItemName,
                Database = Sitecore.Configuration.Factory.GetDatabase(databaseName)
            };

            var creator = new MediaCreator();
            var mediaItem = creator.CreateFromStream(image, sitecorePath + "/" + mediaItemName + extension, options);
            return mediaItem;
        }

        public static long AllowedImageSize
        {
            get
            {
                return Sitecore.Configuration.Settings.GetLongSetting("Media.AllowedImageSize", 524288000L);
            }
        }

        public ImageFormat GetContentType(byte[] imageBytes)
        {
            MemoryStream ms = new MemoryStream(imageBytes);

            using (BinaryReader br = new BinaryReader(ms))
            {
                int maxMagicBytesLength = imageFormatDecoders.Keys.OrderByDescending(x => x.Length).First().Length;

                byte[] magicBytes = new byte[maxMagicBytesLength];

                for (int i = 0; i < maxMagicBytesLength; i += 1)
                {
                    magicBytes[i] = br.ReadByte();

                    foreach (var kvPair in imageFormatDecoders)
                    {
                        if (StartsWith(magicBytes, kvPair.Key))
                        {
                            return kvPair.Value;
                        }
                    }
                }

                throw new ArgumentException("Could not recognise image format", "binaryReader");
            }
        }

        private bool StartsWith(byte[] thisBytes, byte[] thatBytes)
        {
            for (int i = 0; i < thatBytes.Length; i += 1)
            {
                if (thisBytes[i] != thatBytes[i])
                {
                    return false;
                }
            }
            return true;
        }

        private Dictionary<byte[], ImageFormat> imageFormatDecoders = new Dictionary<byte[], ImageFormat>()
            {
                { new byte[]{ 0x42, 0x4D }, ImageFormat.Bmp},
                { new byte[]{ 0x47, 0x49, 0x46, 0x38, 0x37, 0x61 }, ImageFormat.Gif },
                { new byte[]{ 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, ImageFormat.Gif },
                { new byte[]{ 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, ImageFormat.Png },
                { new byte[]{ 0xff, 0xd8 }, ImageFormat.Jpeg },
            };

    }
}
