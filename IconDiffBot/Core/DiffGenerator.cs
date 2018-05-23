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
using ImageMagick;

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
			if (stream == null)
				return null;
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
			if (metadata == null)
				return null;
			var lines = metadata.Split('\n');

			var dmi = new Dmi();

			bool inHeader = true;

			IconState currentState = null;
			foreach(var I in lines)
			{
				if (String.IsNullOrWhiteSpace(I))
					continue;
				var index = I.IndexOf('=');
				var key = I.Substring(0, index).Trim();
				var ip1 = index + 1;
				var value = I.Substring(ip1, I.Length - ip1).Trim();

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
							//if len is 2, it's an empty name
							Name = value.Length == 2 ? String.Empty : value.Substring(1, value.Length - 2)
						};
						dmi.IconStates.Add(currentState);
						break;
					case "hotspot":
						EnsureHeader(false);
						break;
					case "loop":
						EnsureHeader(false);
						currentState.LoopCount = IntValue();
						break;
					case "dirs":
						EnsureHeader(false);
						currentState.Dirs = IntValue();
						break;
					case "frames":
						EnsureHeader(false);
						currentState.Frames = IntValue();
						currentState.FrameDelays.Capacity = currentState.Frames - 1;
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

				foreach (var b in hash)
					sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));

				return sb.ToString();
			}
		}

		/// <summary>
		/// Converts a <paramref name="source"/> image to a <see cref="Models.Image"/>
		/// </summary>
		/// <param name="source">The <see cref="Bitmap"/> to convert</param>
		/// <returns>A <see cref="Models.Image"/> representing <paramref name="source"/></returns>
		static Models.Image ImageToModel(Bitmap source)
		{
			byte[] imageBytes;
			using (var ms = new MemoryStream())
			{
				source.Save(ms, ImageFormat.Png);
				imageBytes = ms.ToArray();
			}
			return new Models.Image
			{
				Data = imageBytes,
				Sha1 = Hash(imageBytes)
			};
		}

		/// <summary>
		/// Copys a region of <paramref name="source"/> to <paramref name="target"/>
		/// </summary>
		/// <param name="source">The source <see cref="Bitmap"/></param>
		/// <param name="target">The target <see cref="Bitmap"/></param>
		/// <param name="srcRect">The source <see cref="Rectangle"/></param>
		/// <param name="targRect">The target <see cref="Rectangle"/></param>
		static void CopyImageRegion(Bitmap source, Bitmap target, Rectangle srcRect, Rectangle targRect)
		{
			using (var g = Graphics.FromImage(target))
				g.DrawImage(source, targRect, srcRect, GraphicsUnit.Pixel);
		}
		
		/// <summary>
		/// Builds a <see cref="Dictionary{TKey, TValue}"/> of <see cref="IconState.Name"/>s mapped to <see cref="Image"/>s given a <paramref name="dmi"/> and it's data <paramref name="stream"/>
		/// </summary>
		/// <param name="dmi">The <see cref="Dmi"/></param>
		/// <param name="stream">The <see cref="Stream"/> containing the <see cref="Dmi"/> data</param>
		/// <returns>A <see cref="Dictionary{TKey, TValue}"/> of <see cref="IconState.Name"/>s mapped to <see cref="Image"/>s given a <paramref name="dmi"/> and it's data <paramref name="stream"/></returns>
		/// <remarks>This code is mostly derived from @lzimann's original icon procs here: https://github.com/Cyberboss/IconDiffBot-python/blob/277e0def44048987d601596b1794354f49dd7412/icons.py#L74 </remarks>
		static Dictionary<string, Models.Image> ExtractImages(Dmi dmi, Stream stream)
		{
			if (dmi == null && stream == null)
				return new Dictionary<string, Models.Image>();

			Rectangle DmiRect() => new Rectangle
			{
				Width = dmi.Width,
				Height = dmi.Height
			};

			Models.Image GetSingleImageForDirs(IEnumerable<Models.Image> images)
			{
				var srcRect = DmiRect();
				var targetRect = DmiRect();

				using (var target = new Bitmap(dmi.Width * images.Count(), dmi.Height, PixelFormat.Format32bppArgb))
				{
					foreach (var I in images)
					{
						using (var tmpImage = new Bitmap(new MemoryStream(I.Data)))
							CopyImageRegion(tmpImage, target, srcRect, targetRect);
						targetRect.X += dmi.Width;
					}

					return ImageToModel(target);
				}
			};

			using (var image = new Bitmap(stream))
			{
				var results = new Dictionary<string, Models.Image>();
				var bySha = new Dictionary<string, Models.Image>();

				var iconsPerLine = image.Width / dmi.Width;

				if (!image.PixelFormat.HasFlag(PixelFormat.Alpha))
					image.MakeTransparent();

				var iconXPos = 0;
				var iconYPos = 0;

				Models.Image GetNextFrameImage()
				{
					var srcRect = DmiRect();
					srcRect.X = iconXPos * dmi.Width;
					srcRect.Y = iconYPos * dmi.Height;

					iconXPos = ++iconXPos % iconsPerLine;
					if (iconXPos == 0)
						++iconYPos;

					using (var target = new Bitmap(srcRect.Width, srcRect.Height, PixelFormat.Format32bppArgb))
					{
						CopyImageRegion(image, target, srcRect, DmiRect());
						return ImageToModel(target);
					}
				};

				foreach (var state in dmi.IconStates)
				{
					Models.Image CreateGifForFrames(IEnumerable<Models.Image> images)
					{
						var index = 0;
						using (var gifCreator = new MagickImageCollection())
						{
							foreach (var I in images)
							{
								var img = new MagickImage(I.Data)
								{
									AnimationDelay = (int)(state.FrameDelays[index++] * 10)
								};
								if (state.LoopCount.HasValue)
									img.AnimationIterations = state.LoopCount.Value;

								gifCreator.Add(img);
							}

							if (state.Rewind)
							{
								//add reverse order
								bool skippedFirst = false;
								foreach(var I in images.Reverse())
								{
									if (!skippedFirst)
										skippedFirst = true;
									else
									{
										var img = new MagickImage(I.Data)
										{
											AnimationDelay = (int)(state.FrameDelays[--index] * 10)
										};
										if (state.LoopCount.HasValue)
											img.AnimationIterations = state.LoopCount.Value;

										gifCreator.Add(img);
									}
								}
							}

							gifCreator.Optimize();
							gifCreator.OptimizeTransparency();

							var result = new Models.Image()
							{
								IsGif = true
							};
							using (var ms = new MemoryStream())
							{
								gifCreator.Write(ms, MagickFormat.Gif);
								result.Data = ms.ToArray();
							}
							result.Sha1 = Hash(result.Data);
							return result;
						}
					};

					var frameSets = new List<List<Models.Image>>(
						Enumerable.Range(0, state.Frames).Select(frame =>
							new List<Models.Image>(
								Enumerable.Range(0, state.Dirs).Select(x => GetNextFrameImage())
						)));

					//collected all dirs and frames
					Models.Image final;
					if (frameSets.Count == 1)
						//static image, one dir
						if (frameSets.First().Count == 1)
							final = frameSets.First().First();
						//static image, multiple dirs
						else
							final = GetSingleImageForDirs(frameSets.First());
					//animated image, one dir
					else if (frameSets.First().Count == 1)
						final = CreateGifForFrames(frameSets.Select(x => x.First()));
					//animated image, multiple dirs
					else
						final = CreateGifForFrames(frameSets.Select(x => GetSingleImageForDirs(x)));


					if (bySha.TryGetValue(final.Sha1, out Models.Image olderOne))
						final = olderOne;
					else
						bySha.Add(final.Sha1, final);

					var name = state.Name;
					if (results.ContainsKey(name))
					{
						var baseName = name;
						int counter = 1;
						do
							name = String.Format(CultureInfo.InvariantCulture, "{0}-{1}", baseName, ++counter);
						while (results.ContainsKey(name));
					}
					results.Add(name, final);
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

			Dictionary<string, Models.Image> beforeDic, afterDic;
			try
			{
				beforeDic = ExtractImages(beforeDmi, before);
				afterDic = ExtractImages(afterDmi, after);
			}
			// weird issue with some .dmis
			catch (ArgumentException)
			{
				return null;
			}

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
