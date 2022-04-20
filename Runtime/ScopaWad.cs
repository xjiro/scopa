using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using Scopa.Formats.Texture.Wad;
using Scopa.Formats.Texture.Wad.Lumps;
using Scopa.Formats.Id;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Scopa {
    /// <summary> main class for WAD import and export </summary>
    public static class ScopaWad {

        // static buffers for all reading and writing operations, to try to reduce GC
        static Color32[] palette = new Color32[256];
        static List<ColorBucket> buckets = new List<ColorBucket>(128*128);
        static List<ColorBucket> newBuckets = new List<ColorBucket>(256);
        static Texture2D resizedTexture;

        #region WAD Reading

        public static WadFile ParseWad(string fileName)
        {
            using (var fStream = System.IO.File.OpenRead(fileName))
            {
                var newWad = new WadFile(fStream);
                newWad.Name = System.IO.Path.GetFileNameWithoutExtension(fileName);
                return newWad;
            }
        }

        public static List<Texture2D> BuildWadTextures(WadFile wad, ScopaWadConfig config) {
            if ( wad == null || wad.Entries == null || wad.Entries.Count == 0) {
                Debug.LogError("Couldn't parse WAD file " + wad.Name);
            }

            var textureList = new List<Texture2D>();

            foreach ( var entry in wad.Entries ) {
                if ( entry.Type != LumpType.RawTexture && entry.Type != LumpType.MipTexture )
                    continue;

                var texData = (wad.GetLump(entry) as MipTexture);
                // Debug.Log(entry.Name);
                // Debug.Log( "BITMAP: " + string.Join(", ", texData.MipData[0].Select( b => b.ToString() )) );
                // Debug.Log( "PALETTE: " + string.Join(", ", texData.Palette.Select( b => b.ToString() )) );

                // Half-Life GoldSrc textures use individualized 256 color palettes; Quake textures will have a reference to the hard-coded Quake palette
                var width = System.Convert.ToInt32(texData.Width);
                var height = System.Convert.ToInt32(texData.Height);

                for (int i=0; i<256; i++) {
                    palette[i] = new Color32( texData.Palette[i*3], texData.Palette[i*3+1], texData.Palette[i*3+2], 0xff );
                }

                // the last color is reserved for transparency
                var paletteHasTransparency = false;
                if ( (palette[255].r == QuakePalette.Data[255*3] && palette[255].g == QuakePalette.Data[255*3+1] && palette[255].b == QuakePalette.Data[255*3+2])
                    || (palette[255].r == 0x00 && palette[255].g == 0x00 && palette[255].b == 0xff) ) {
                    paletteHasTransparency = true;
                    palette[255] = new Color32(0x00, 0x00, 0x00, 0x00);
                }
                
                var mipSize = texData.MipData[0].Length;
                var pixels = new Color32[mipSize];
                var usesTransparency = false;

                // for some reason, WAD texture bytes are flipped? have to unflip them for Unity
                for( int y=0; y < height; y++) {
                    for (int x=0; x < width; x++) {
                        int paletteIndex = texData.MipData[0][(height-1-y)*width + x];
                        pixels[y*width+x] = palette[paletteIndex];
                        if ( !usesTransparency && paletteHasTransparency && paletteIndex == 255) {
                            usesTransparency = true;
                        }
                    }
                }

                // we have all pixel color data now, so we can build the Texture2D
                var newTexture = new Texture2D( width, height, usesTransparency ? TextureFormat.RGBA32 : TextureFormat.RGB24, true, config.linearColorspace);
                newTexture.name = texData.Name.ToLowerInvariant();
                newTexture.SetPixels32(pixels);
                newTexture.alphaIsTransparency = usesTransparency;
                newTexture.filterMode = config.filterMode;
                newTexture.anisoLevel = config.anisoLevel;
                newTexture.Apply();
                if ( config.compressTextures ) {
                    newTexture.Compress(false);
                }
                textureList.Add( newTexture );
                
            }

            return textureList;
        }

        public static Material BuildMaterialForTexture( Texture2D texture, ScopaWadConfig config ) {
            var material = texture.alphaIsTransparency ? 
                (config.alphaTemplate != null ? config.alphaTemplate : GenerateDefaultMaterialAlpha())
                : (config.opaqueTemplate != null ? config.opaqueTemplate : GenerateDefaultMaterialOpaque());
            material.name = texture.name;
            material.mainTexture = texture;

            return material;
        }

        public static Material GenerateDefaultMaterialOpaque() {
            // TODO: URP, HDRP
            var material = new Material( Shader.Find("Standard") );
            material.SetFloat("_Glossiness", 0.1f);
            return material;
        }

        public static Material GenerateDefaultMaterialAlpha() {
            // TODO: URP, HDRP
            var material = new Material( Shader.Find("Standard") );
            material.SetFloat("_Glossiness", 0.1f);
            material.SetFloat("_Mode", 1);
            material.EnableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = 2450;
            return material;
        }

        #endregion
        #region WAD Writing

        public static void SaveWad3File(string filepath, ScopaWadCreator wadConfig) {
            var wadData = GenerateWad3Data( Path.GetFileNameWithoutExtension(filepath), wadConfig);
            using (var fStream = System.IO.File.OpenWrite(filepath))
            {
                wadData.Write(fStream);
            }
            Debug.Log("saved WAD3 file to " + filepath);
        }

        static WadFile GenerateWad3Data(string wadName, ScopaWadCreator wadConfig) {
            Debug.Log("started generating wad data for " + wadName);
            var newWad = new WadFile(Formats.Texture.Wad.Version.Wad3);
            newWad.Name = wadName;
            foreach ( var mat in wadConfig.materials ) {
                var texName = mat.name.ToLowerInvariant();
                texName = texName.Substring(0, Mathf.Min(texName.Length, 16) ); // texture names are limited to 16 characters
                Debug.Log("started working on " + texName);

                var mipTex = new MipTextureLump();
                mipTex.Name = texName;
                mipTex.Width = System.Convert.ToUInt32(mat.mainTexture.width / (int)wadConfig.resolution);
                mipTex.Height = System.Convert.ToUInt32(mat.mainTexture.height / (int)wadConfig.resolution);
                mipTex.NumMips = 4; // all wad3 textures always have 3 mips
                Debug.Log($"{mipTex.Name} is {mipTex.Width} x {mipTex.Height}");

                mipTex.MipData = QuantizeToMipmap( (Texture2D)mat.mainTexture, mat.color, (int)wadConfig.resolution, out var palette );

                mipTex.Palette = new byte[palette.Length * 3];
                for( int i=0; i<palette.Length; i++) {
                    mipTex.Palette[i*3] = palette[i].r;
                    mipTex.Palette[i*3+1] = palette[i].g;
                    mipTex.Palette[i*3+2] = palette[i].b;
                }

                newWad.AddLump( texName, mipTex );
            }
            Debug.Log("finished generating wad data");
            return newWad;
        }

        // median cut color palette quanitization code
        // adapted from https://github.com/bacowan/cSharpColourQuantization/blob/master/ColourQuantization/MedianCut.cs
        // used under Unlicense License
        /// <summary> actual palette color count will be -=1, to insert blue 255 at the end for transparency </summary>
        public static byte[][] QuantizeToMipmap(Texture2D original, Color colorTint, int resizeFactor, out Color32[] fixedPalette, int paletteColorCount = 256)
        {
            // we have to do this in two passes, with two render textures

            // pass 1: tint the texture, generate the color palette
            ResizeCopyToBuffer(original, colorTint, original.width / resizeFactor, original.height / resizeFactor); 
            
            // we use Color32 because it's faster + avoids floating point comparison issues with HasColor() + we need to write out bytes anyway
            var colors = resizedTexture.GetPixels32();
            // for(int i=0; i<colors.Length; i++) {
            //     colors[i].r = System.Convert.ToByte( Mathf.RoundToInt(colors[i].r * colorTint.r) );
            //     colors[i].g = System.Convert.ToByte( Mathf.RoundToInt(colors[i].g * colorTint.g) );
            //     colors[i].b = System.Convert.ToByte( Mathf.RoundToInt(colors[i].b * colorTint.b) );
            //     colors[i].a = 0xff;
            // }

            buckets.Clear();
            buckets.Add( new ColorBucket(colors) );
            paletteColorCount -= 1; // reserve space for blue 255

            // build color buckets / palette groups
            // TODO: switch quantizer to https://github.com/JeremyAnsel/JeremyAnsel.ColorQuant/blob/master/JeremyAnsel.ColorQuant/JeremyAnsel.ColorQuant/WuColorQuantizer.cs
            // or maybe https://github.com/bacowan/cSharpColourQuantization/blob/master/ColourQuantization/Octree.cs
            int iterations = 0;
            while (buckets.Count < paletteColorCount && iterations < paletteColorCount) {
                newBuckets.Clear();
                for (var i = 0; i < buckets.Count; i++) {
                    if (newBuckets.Count + (buckets.Count - i) < paletteColorCount) {
                        buckets[i].Split(out var b1, out var b2);
                        newBuckets.Add(b1);
                        newBuckets.Add(b2);
                    }
                    else {
                        newBuckets.AddRange(buckets.GetRange(i, buckets.Count - i));
                        break;
                    }
                }
                buckets.Clear();
                buckets.AddRange( newBuckets );
                iterations++;
            }
            Debug.Log($"color palette has {buckets.Count} colors");

            // pad out any unused palette slots
            // actually, let's disable transparency for now
            var emptyBucket = new ColorBucket( new Color32[] { new Color32(0x88, 0x88, 0x88, 0x88) } );
            while ( buckets.Count < paletteColorCount+1 ) {
                buckets.Add( emptyBucket );
            }

            // convert buckets to color palette
            // fixedPalette = buckets.Select( bucket => bucket.Color ).ToArray();
            
            // DEBUG: grayscale palette looks great... so the problem is the palette generation code lol
            fixedPalette = new Color32[256];
            for (int i=0; i<256; i++) {
                // fixedPalette[i] = new Color32( QuakePalette.Data[i*3], QuakePalette.Data[i*3+1], QuakePalette.Data[i*3+2], 0xff );
                fixedPalette[i] = new Color32( 
                    System.Convert.ToByte(Mathf.RoundToInt(i * colorTint.r)), 
                    System.Convert.ToByte(Mathf.RoundToInt(i * colorTint.g)), 
                    System.Convert.ToByte(Mathf.RoundToInt(i * colorTint.b)), 
                    0xff);
            }
            
            // pass 2: now that we have a color palette, use render texture to palettize AND generate mipmaps all at once
            var width = resizedTexture.width;
            var height = resizedTexture.height;
            var mipmap = new byte[4][];
            ResizeCopyToBuffer(original, colorTint, width, height, fixedPalette);

            for( int mip=0; mip<4; mip++) {
                //Debug.Log("quanitizing mip#" + mip.ToString() );
                int factor = Mathf.RoundToInt( Mathf.Pow(2, mip) );
                mipmap[mip] = new byte[ (width/factor) * (height/factor) ];
                
                var indices = resizedTexture.GetPixels32(mip);
                for( int y=0; y<height/factor; y++) {
                    for( int x=0; x<width/factor; x++) {
                        // textures are vertically flipped, so have to unflip them? idk
                        mipmap[mip][(height/factor-1-y)*width/factor + x] = indices[y*width/factor+x].r;
                    }
                }
            }
            return mipmap;
        }

        // code from https://github.com/ababilinski/unity-gpu-texture-resize
        // and https://support.unity.com/hc/en-us/articles/206486626-How-can-I-get-pixels-from-unreadable-textures-
        // TODO: convert to https://forum.unity.com/threads/closest-color-shader.668689/ ?

        public static void ResizeCopyToBuffer(Texture2D source, Color tint, int targetX, int targetY, Color32[] palette = null) {
            RenderTexture tmp = RenderTexture.GetTemporary( 
                targetX,
                targetY,
                0,
                RenderTextureFormat.Default,
                RenderTextureReadWrite.sRGB, 
                1
            );

            // Blit the pixels on texture to the RenderTexture
            if ( palette != null ) {
                var mat = new Material( Shader.Find("Hidden/PalettizeBlit") );
                // mat.SetColor("_Color", tint);
                mat.SetColorArray( "_Colors", palette.Select( c => new Color(c.r / 255f, c.g / 255f, c.b / 255f) ).ToArray() );
                Graphics.Blit(source, tmp, mat);
            } else {
                var mat = new Material( Shader.Find("Hidden/BlitTint") );
                mat.SetColor("_Color", tint);
                Graphics.Blit(source, tmp, mat);
                // Graphics.Blit(source, tmp);
            }

            // Backup the currently set RenderTexture
            RenderTexture previous = RenderTexture.active;

            // Set the current RenderTexture to the temporary one we created
            RenderTexture.active = tmp;

            // Create a new readable Texture2D to copy the pixels to it
            resizedTexture = new Texture2D(targetX, targetY, TextureFormat.RGB24, palette != null ? 4 : 0, true );

            // Copy the pixels from the RenderTexture to the new Texture
            resizedTexture.ReadPixels(new Rect(0, 0, targetX, targetY), 0, 0, palette != null);
            resizedTexture.Apply();

            // Reset the active RenderTexture
            RenderTexture.active = previous;

            // Release the temporary RenderTexture
            RenderTexture.ReleaseTemporary(tmp);
        }

        public class ColorBucket
        {
            private readonly IDictionary<Color32, int> colors;

            public Color32 Color { get; }

            public ColorBucket(IEnumerable<Color32> colors)
            {
                this.colors = colors.ToLookup(c => c)
                    .ToDictionary(c => c.Key, c => c.Count());
                this.Color = Average(this.colors);
            }

            public ColorBucket(IEnumerable<KeyValuePair<Color32, int>> enumerable)
            {
                this.colors = enumerable.ToDictionary(c => c.Key, c => c.Value);
                this.Color = Average(this.colors);
            }

            private static Color32 Average(IEnumerable<KeyValuePair<Color32, int>> colors)
            {
                var totals = colors.Sum(c => c.Value);
                return new Color32(
                    r: System.Convert.ToByte( Mathf.RoundToInt(colors.Sum(c => System.Convert.ToSingle(c.Key.r) * c.Value) / Mathf.Max(1, totals)) ),
                    g: System.Convert.ToByte( Mathf.RoundToInt(colors.Sum(c => System.Convert.ToSingle(c.Key.g) * c.Value) / Mathf.Max(1, totals)) ),
                    b: System.Convert.ToByte( Mathf.RoundToInt(colors.Sum(c => System.Convert.ToSingle(c.Key.b) * c.Value) / Mathf.Max(1, totals)) ),
                    a: 0xff
                );
            }

            public bool HasColor(Color32 color)
            {
                return colors.ContainsKey(color);
            }

            public void Split(out ColorBucket bucket1, out ColorBucket bucket2)
            {
                var redRange = colors.Keys.Max(c => c.r) - colors.Keys.Min(c => c.r);
                var greenRange = colors.Keys.Max(c => c.g) - colors.Keys.Min(c => c.g);
                var blueRange = colors.Keys.Max(c => c.b) - colors.Keys.Min(c => c.b);

                Func<Color32, int> sorter;
                if (redRange > greenRange)
                {
                    if (redRange > blueRange)
                    {
                        sorter = c => c.r;
                    }
                    else
                    {
                        sorter = c => c.b;
                    }
                }
                else
                {
                    if (greenRange > blueRange)
                    {
                        sorter = c => c.g;
                    }
                    else
                    {
                        sorter = c => c.b;
                    }
                }

                var sorted = colors.OrderBy(c => sorter(c.Key));

                var firstBucketCount = sorted.Count() / 2;

                bucket1 = new ColorBucket(sorted.Take(firstBucketCount));
                bucket2 = new ColorBucket(sorted.Skip(firstBucketCount));
            }
        }

        #endregion
    }
}