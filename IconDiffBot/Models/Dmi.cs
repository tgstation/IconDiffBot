using System;
using System.Collections.Generic;

namespace IconDiffBot.Models
{
	/// <summary>
	/// Represents a .dmi file
	/// </summary>
    sealed class Dmi
    {
		/// <summary>
		/// The <see cref="System.Version"/> of the file
		/// </summary>
		public Version Version { get; set; }

		/// <summary>
		/// The width of icons in the file
		/// </summary>
		public int Width { get; set; }

		/// <summary>
		/// The height of icons in the file
		/// </summary>
		public int Height { get; set; }

		/// <summary>
		/// The <see cref="IconStates"/> in the file
		/// </summary>
		public List<IconState> IconStates { get; } = new List<IconState>();
    }
}
