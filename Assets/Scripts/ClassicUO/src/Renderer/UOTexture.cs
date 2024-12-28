#region license

// Copyright (C) 2020 ClassicUO Development Community on Github
// 
// This project is an alternative client for the game Ultima Online.
// The goal of this is to develop a lightweight client considering
// new technologies.
// 
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
// 
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU General Public License for more details.
// 
//  You should have received a copy of the GNU General Public License
//  along with this program.  If not, see <https://www.gnu.org/licenses/>.

#endregion

using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Renderer
{
    internal class UOTexture : Texture2D
    {
        public UOTexture(int width, int height)
            : base(Client.Game.GraphicsDevice, width, height, false, SurfaceFormat.Color)
        {
            Ticks = Time.Ticks + 3000;
        }

        public long Ticks { get; set; }
        public uint[] Data { get; private set; }

        // MobileUO: added keepData optional parameter
        public void PushData(uint[] data, bool keepData = false)
        {
            if (keepData)
            {
                Data = data;
            }

            SetData(data);
        }

        // MobileUO: logic changes for Unity
        public bool Contains(int x, int y, bool pixelCheck = true)
        {
            // MobileUO: don't keep Data != null or else clicks won't work
            if (x >= 0 && y >= 0 && x < Width && y < Height)
            {
                if (!pixelCheck)
                {
                    return true;
                }

                if (UnityTexture == null)
                    return false;
                
                int pos = y * Width + x;
                return GetDataAtPos(pos) != 0;
            }

            return false;
        }

        // MobileUO: Used for Contains checks in texture using Unity's own texture data, instead of keeping a copy of the data in _data field
        private uint GetDataAtPos(int pos)
        {
            //The index calculation here is the same as in Texture2D.SetData
            var width = Width;
            int x = pos % width;
            int y = pos / width;
            y *= width;
            var index = y + (width - x - 1);
            
            var data = (UnityTexture as UnityEngine.Texture2D).GetRawTextureData<uint>();
            //We reverse the index because we had already reversed it in Texture2D.SetData
            var reversedIndex = data.Length - index - 1;
            if (reversedIndex < data.Length && reversedIndex >= 0)
            {
                return data[reversedIndex];
            }

            return 0;
        }
    }
}