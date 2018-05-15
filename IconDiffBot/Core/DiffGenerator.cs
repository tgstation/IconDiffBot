using MetadataExtractor;
using IconDiffBot.Models;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using Stream = System.IO.Stream;
using MemoryStream = System.IO.MemoryStream;

namespace IconDiffBot.Core
{
	/// <inheritdoc />
	sealed class DiffGenerator : IDiffGenerator
	{
		/// <summary>
		/// Converts a <paramref name="stream"/> of a .dmi to it's metadata string
		/// </summary>
		/// <param name="stream">The <see cref="Stream"/> to parse</param>
		/// <returns>The .dmi metadata string</returns>
		static string StreamToMetadataString(Stream stream)
		{
			var metadata = ImageMetadataReader.ReadMetadata(stream);
			const string DmiHeader = "# BEGIN DMI";
			var description = metadata.SelectMany(x => x.Tags).First(x => x.Description.Contains(DmiHeader)).Description;
			var startIndex = description.IndexOf(DmiHeader, StringComparison.InvariantCulture) + DmiHeader.Length;
			var length = description.IndexOf("# END DMI", StringComparison.InvariantCulture) - startIndex;
			return description.Substring(startIndex, length);
		}

		/// <summary>
		/// Create a <see cref="Dmi"/> given a <paramref name="metadata"/> <see cref="string"/> without the headers
		/// </summary>
		/// <param name="metadata">The <see cref="Dmi"/> metadata <see cref="string"/></param>
		/// <returns>The <see cref="Dmi"/> built from <paramref name="metadata"/></returns>
		static Dmi BuildDmi(string metadata)
		{
			var lines = metadata.Split('\n');

			var dmi = new Dmi();

			bool inHeader = true;

			IconState currentState = null;
			foreach(var I in lines)
			{
				if (String.IsNullOrWhiteSpace(I))
					continue;
				var splits = I.Split('=');
				var key = splits[0].Trim();
				var value = splits[1].Trim();

				int IntValue() => Convert.ToInt32(value, CultureInfo.InvariantCulture);

				void EnsureHeader(bool expectedInHeader)
				{
					if(expectedInHeader != inHeader)
						throw new InvalidOperationException(String.Format(CultureInfo.InvariantCulture, "Expected to be {0} DMI header for key {1} but was not!", expectedInHeader, key));
				}

				switch (key)
				{
					case "version":
						EnsureHeader(true);
						dmi.Version = Version.Parse(value);
						break;
					case "width":
						EnsureHeader(true);
						dmi.Width = IntValue();
						break;
					case "height":
						EnsureHeader(true);
						dmi.Height = IntValue();
						break;
					case "state":
						inHeader = false;
						currentState = new IconState
						{
							Name = value
						};
						dmi.IconStates.Add(currentState);
						break;
					case "dirs":
						EnsureHeader(false);
						currentState.Dirs = IntValue();
						break;
					case "frames":
						EnsureHeader(false);
						currentState.FrameDelays.Capacity = IntValue();
						break;
					case "delay":
						EnsureHeader(false);
						var delaySplits = value.Split(',');
						currentState.FrameDelays.AddRange(delaySplits.Select(x => Convert.ToSingle(x, CultureInfo.InvariantCulture)));
						break;
					case "rewind":
						EnsureHeader(false);
						currentState.Rewind = IntValue() != 0;
						break;
					default:
						throw new NotSupportedException(String.Format(CultureInfo.InvariantCulture, "Unknown DMI {0} key: {1}", inHeader ? "header" : "state", key));
				}
			}

			return dmi;
		}

		/// <summary>
		/// Get the SHA-1 hash of a given <paramref name="input"/>
		/// </summary>
		/// <param name="input">The <see cref="byte"/> array to hash</param>
		/// <returns>The SHA-1 hash of <paramref name="input"/></returns>
		static string Hash(byte[] input)
		{
#pragma warning disable CA5350 // Do not use insecure cryptographic algorithm SHA1.
			using (var sha1 = new SHA1Managed())
#pragma warning restore CA5350 // Do not use insecure cryptographic algorithm SHA1.
			{
				var hash = sha1.ComputeHash(input);
				var sb = new StringBuilder(hash.Length * 2);

				foreach (byte b in hash)
					sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));

				return sb.ToString();
			}
		}

		/// <summary>
		/// Builds a <see cref="Dictionary{TKey, TValue}"/> of <see cref="IconState.Name"/>s mapped to <see cref="Image"/>s given a <paramref name="dmi"/> and it's data <paramref name="stream"/>
		/// </summary>
		/// <param name="dmi">The <see cref="Dmi"/></param>
		/// <param name="stream">The <see cref="Stream"/> containing the <see cref="Dmi"/> data</param>
		/// <returns>A <see cref="Dictionary{TKey, TValue}"/> of <see cref="IconState.Name"/>s mapped to <see cref="Image"/>s given a <paramref name="dmi"/> and it's data <paramref name="stream"/></returns>
		Dictionary<string, Models.Image> ExtractImages(Dmi dmi, Stream stream)
		{
			using (var image = new Bitmap(stream))
			{
				var iconsPerLine = image.Width / dmi.Width;
				var totalLines = image.Height / dmi.Height;

				if (!image.PixelFormat.HasFlag(PixelFormat.Alpha))
					image.MakeTransparent();

				var results = new Dictionary<string, Models.Image>();
				var bySha = new Dictionary<string, Models.Image>();
				var iconCount = 0;
				var nameCount = 1;
				var skipNaming = 0;

				for (var line = 0; line < totalLines; ++line) {
					var icon = 0;
					while (icon < iconsPerLine && iconCount < dmi.IconStates.Count)
					{
						var state = dmi.IconStates[iconCount];
						var name = state.Name;
						if (skipNaming > 0)
						{
							if (nameCount > 0)
								name = String.Format(CultureInfo.InvariantCulture, "{0} F{1}", name, nameCount++);
							if (--skipNaming == 0)
								++iconCount;
						}
						else
						{
							skipNaming = state.FrameDelays.Count;
							if (skipNaming == 0)
								++iconCount;
							else
								nameCount = 1;
						}

						var srcRect = new Rectangle
						{
							X = icon * dmi.Width,
							Y = line * dmi.Height,
							Width = dmi.Width,
							Height = dmi.Height
						};

						byte[] imageBytes;
						using (var target = new Bitmap(dmi.Width, dmi.Height, PixelFormat.Format32bppArgb)) {
							using (var g = Graphics.FromImage(image))
								g.DrawImage(image, new Rectangle(0, 0, dmi.Width, dmi.Height), srcRect, GraphicsUnit.Pixel);
							using (var ms = new MemoryStream())
							{
								target.Save(ms, ImageFormat.Png);
								imageBytes = ms.ToArray();
							}
						}
						var final = new Models.Image
						{
							Data = imageBytes,
							Sha1 = Hash(imageBytes)
						};

						if (bySha.TryGetValue(final.Sha1, out Models.Image olderOne))
							final = olderOne;
						else
							bySha.Add(final.Sha1, final);

						results.Add(name, final);
					}
				}

				return results;
			}
		}

		/// <inheritdoc />
		public List<IconDiff> GenerateDiffs(Stream before, Stream after)
		{
			var beforeString = StreamToMetadataString(before);
			var afterString = StreamToMetadataString(after);

			var beforeDmi = BuildDmi(beforeString);
			var afterDmi = BuildDmi(afterString);

			var beforeDic = ExtractImages(beforeDmi, before);
			var afterDic = ExtractImages(afterDmi, after);

			var results = new List<IconDiff>();

			foreach(var I in beforeDic)
			{
				var beforeState = I.Value;
				if (afterDic.TryGetValue(I.Key, out Models.Image afterState))
				{
					if (beforeState.Sha1 != afterState.Sha1)
						//changes
						results.Add(new IconDiff
						{
							Before = beforeState,
							After = afterState,
							StateName = I.Key
						});
					afterDic.Remove(I.Key);
					continue;
				}

				//removed
				results.Add(new IconDiff
				{
					Before = beforeState,
					StateName = I.Key
				});
			}

			foreach(var I in afterDic)
				//everything here is new
				results.Add(new IconDiff
				{
					After = I.Value,
					StateName = I.Key
				});

			return results;
		}
	}
}
