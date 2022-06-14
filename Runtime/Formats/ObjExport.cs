using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Globalization;
using System.IO;
using System.Text;

// OBJ Export is based on:
// ExportOBJ from old defunct Unity wiki (RIP) https://wiki.unity3d.com/index.php/ExportOBJ
// subsequent edits by Matt Rix https://gist.github.com/MattRix/0522c27ee44c0fbbdf76d65de123eeff
// main change here was to convert to a static utility class... -RY, 29 June 2019
// and now optimized a little + updated to bake skinned mesh renderers too -RY, May 2022

namespace Scopa {

    /// <summary> this has a lot of TrenchBroom specific optimizations... as-is, it's not a general use OBJ export library </summary>
	public class ObjExport
	{
		private static int StartIndex = 0;
		
        /// <summary> can handle MeshFilter or SkinnedMeshRenderers too </summary>
		static string MeshToString(GameObject go, Transform t, List<Material> materialLibrary ) 
		{	
			int numVertices = 0;
			Mesh m = null;

            if ( go.TryGetComponent<MeshFilter>(out var mf) ) {
                m = mf.sharedMesh;
            } else if ( go.TryGetComponent<SkinnedMeshRenderer>(out var smr) && smr.enabled) {
                var newMesh = new Mesh();
                smr.BakeMesh(newMesh, true);
                m = newMesh;
            }

			if (!m)
			{
				return $"#### no Mesh Filter or Skinned Mesh Renderer found on {go.name} ####";
			}

			Material[] mats = go.GetComponent<Renderer>().sharedMaterials;
			
			StringBuilder sb = new StringBuilder();
			
            for(int i=0; i<m.vertices.Length; i++)
			{
				numVertices++;
                var newVert = t.TransformPoint(m.vertices[i]);
                // use CultureInfo.InvariantCulture because some user languages use commas as decimal markers, but that is invalid OBJ syntax
                // also, SWIZZLE Y AND Z FOR QUAKE ENGINE
				// sb.AppendFormat( CultureInfo.InvariantCulture, "v {0} {1} {2}\n", -m.vertices[i].x, m.vertices[i].z, m.vertices[i].y );
                sb.AppendFormat( CultureInfo.InvariantCulture, "v {0} {1} {2}\n", -newVert.x, newVert.y, newVert.z );
			}
			sb.Append("\n");

            // TrenchBroom doesn't care about normals
            // for (int i = 0; i < m.normals.Length; i++) 
			// {
            //     // also, SWIZZLE Y AND Z FOR QUAKE ENGINE
			// 	sb.AppendFormat( CultureInfo.InvariantCulture, "vn {0} {1} {2}\n", -m.normals[i].x, m.normals[i].y, m.normals[i].z );
			// }
			// sb.Append("\n");

            for (int i = 0; i < m.uv.Length; i++) 
			{
                sb.AppendFormat( CultureInfo.InvariantCulture, "vt {0} {1}\n", m.uv[i].x, m.uv[i].y );
			}

			for (int material=0; material < m.subMeshCount; material++) 
			{
				sb.Append("\n");
                
				sb.AppendLine($"usemtl { GetMaterialFilename(mats[material]) }");
                if ( !materialLibrary.Contains(mats[material]) ) {
                    materialLibrary.Add( mats[material]);
                }

				// sb.Append("usemap ").Append(mats[material].name).Append("\n");

                // output material string
                // mtlSB.AppendLine($"newmtl {mats[material].name}{go.GetInstanceID()}");
                // mtlSB.AppendLine("Ka 1.000 1.000 1.000");
                // mtlSB.AppendLine($"Kd 1.000 1.000 1.000");
				
				int[] triangles = m.GetTriangles(material);
				for (int i=0;i<triangles.Length;i+=3) {
					sb.AppendFormat(CultureInfo.InvariantCulture, "f {0}/{0}/{0} {1}/{1}/{1} {2}/{2}/{2}\n", 
											triangles[i+2]+1+StartIndex, triangles[i+1]+1+StartIndex, triangles[i]+1+StartIndex);
				}
			}
			
			StartIndex += numVertices;

			return sb.ToString();
		}

        static string GetMaterialFilename(Material mat) {
            return (mat.name + "-" + mat.mainTexture.name).Replace(" ", "_");
        }
		
		public static string SaveObjFile(string fileName, GameObject gameObject, Vector3 scale, bool makeSubmeshes = false)
		{
			if (gameObject == null)
			{
				Debug.LogWarning("ObjExport: no game object defined, nothing to export");
				return null;
			}
			
			string meshName = gameObject.name;
            var materialLibrary = new List<Material>();
			
			// start
			StartIndex = 0;
			StringBuilder meshString = new StringBuilder();
			meshString.Append("#" + meshName + ".obj"
							+ "\n#" + System.DateTime.Now.ToLongDateString() 
							+ "\n#" + System.DateTime.Now.ToLongTimeString()
							+ "\n#-------" 
							+ "\n\n");

			// process all gameobjects, even the children (see ProcessTransform() )
            Transform t = gameObject.transform;
            var oldPos = t.position;
            var oldRot = t.rotation;
            var oldScale = t.localScale;
            t.position = Vector3.zero;
            t.rotation = Quaternion.Euler(0, -90, 0); // hardcoded for Quake axes
            t.localScale = scale;

            if (!makeSubmeshes)
                meshString.Append("g ").Append(t.name).Append("\n");
            meshString.Append(ProcessTransform(t, materialLibrary, makeSubmeshes));

            t.position = oldPos;
            t.rotation = oldRot;
            t.localScale = oldScale;
			
			WriteToFile(meshString.ToString(), fileName);
            if ( materialLibrary.Count > 0)
                WriteTextures( Directory.GetParent(Path.GetDirectoryName(fileName)).ToString() + "/textures/", materialLibrary );
            // TODO: create textures folder if it doesn't exist already
			
			// end
			StartIndex = 0;
			Debug.Log("ObjExport: saved .OBJ to " + fileName);
			return fileName;
		}
		
		static string ProcessTransform(Transform t, List<Material> materialLibrary, bool makeSubmeshes)
		{
			StringBuilder meshString = new StringBuilder();
			
			meshString.Append("#" + t.name
							+ "\n#-------" 
							+ "\n");
			
			if (makeSubmeshes)
				meshString.Append("g ").Append(t.name).Append("\n");
			
			meshString.Append(ObjExport.MeshToString(t.gameObject, t, materialLibrary));
			
			for(int i = 0; i < t.childCount; i++)
			{
                var child = t.GetChild(i);
                if ( child.gameObject.activeSelf )
				    meshString.Append(ProcessTransform(child, materialLibrary, makeSubmeshes));
			}
			
			return meshString.ToString();
		}
		
		static void WriteToFile(string s, string filename)
		{
			using (StreamWriter sw = new StreamWriter(filename)) 
			{
				sw.Write(s);
			}
		}

        /// <summary> resizeFactor must be a power of two number, larger factor = smaller texture (e.g. 8 = 1/8 size)</summary>
        static void WriteTextures(string folderPath, List<Material> materials, int resizeFactor = 16, int jpgQuality = 50) {
            foreach(var mat in materials) {
                ScopaWad.ResizeCopyToBuffer( (Texture2D)mat.mainTexture, mat.color, mat.mainTexture.width / resizeFactor, mat.mainTexture.height / resizeFactor);
                var bytes = ImageConversion.EncodeToJPG( ScopaWad.resizedTexture, jpgQuality);
                File.WriteAllBytes( $"{ folderPath }/{ GetMaterialFilename(mat) }.jpg", bytes);
            }
        }

        
	}

}