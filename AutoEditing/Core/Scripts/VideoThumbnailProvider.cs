using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Core.Scripts;

internal static class VideoThumbnailProvider
{
	[StructLayout(LayoutKind.Sequential)]
	private struct NativeSize { public int Width; public int Height; }

	[Flags]
	private enum ImageFlags { BiggerSizeOk = 1, ThumbnailOnly = 8 }

	[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
	private interface IShellItemImageFactory
	{
		void GetImage(NativeSize size, ImageFlags flags, out IntPtr bitmap);
	}

	[DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
	private static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext, [MarshalAs(UnmanagedType.LPStruct)] Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

	[DllImport("gdi32.dll")]
	private static extern bool DeleteObject(IntPtr handle);

	public static ImageSource Load(string path, int width = 320, int height = 180)
	{
		IShellItemImageFactory factory;
		SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out factory);
		IntPtr bitmap = IntPtr.Zero;
		try
		{
			factory.GetImage(new NativeSize { Width = width, Height = height }, ImageFlags.BiggerSizeOk, out bitmap);
			if (bitmap == IntPtr.Zero) return null;
			BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
			source.Freeze();
			return source;
		}
		finally
		{
			if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
			if (factory != null && Marshal.IsComObject(factory)) Marshal.FinalReleaseComObject(factory);
		}
	}
}
