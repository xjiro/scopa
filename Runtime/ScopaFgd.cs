using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using Mesh = UnityEngine.Mesh;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Scopa {

    /// <summary> magically binds this public class member to an FGD *and* populates it with entity data on import 
    /// and, if it has a [Tooltip] attribute, it also pulls that into the FGD as help text! wow! <br />
    /// REQUIREMENTS: (1) the class variable MUST be public
    /// (2) the MonoBehaviour MUST implement IScopaEntityImport </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class BindFgd : Attribute {
        public string propertyKey, editorLabel;
        public VarType propertyType;

        public BindFgd( string propertyName, VarType propertyType ) {
            this.propertyKey = propertyName;
            this.editorLabel = propertyName;
            this.propertyType = propertyType;
        }

        public BindFgd( string propertyName, VarType propertyType, string editorLabel ) {
            this.propertyKey = propertyName;
            this.editorLabel = editorLabel;
            this.propertyType = propertyType;
        }

        public enum VarType {
            String,
            Bool,
            Int,
            IntScaled,
            Float,
            FloatScaled,
            /// <summary>applies axis correction and default MAP scaling factor</summary>
            Vector3Scaled,
            /// <summary>applies axis correction BUT no MAP scaling correction</summary>
            Vector3Unscaled,
            /// <summary>bind to Vector3 as axis-corrected euler angles</summary>
            Angles3D,
            Choices
        }
    }

    /// <summary>main class for core Scopa FGD functions</summary>
    public static class ScopaFgd {
        public static void ExportFgdFile(ScopaFgdConfig fgd, string filepath, bool exportModels = true) {
            var fgdText = fgd.ToString();
            var encoding = new System.Text.UTF8Encoding(false); // no BOM
            System.IO.File.WriteAllText(filepath, fgdText, encoding);
            Debug.Log("wrote FGD to " + filepath);

            if ( exportModels ) {
                ExportObjModels(fgd, filepath);
                Debug.Log("wrote OBJs to " + filepath);
            }
        }

        public static void ExportObjModels(ScopaFgdConfig fgd, string filepath) {
            var folder = Path.GetDirectoryName(filepath) + "/preview/";

            // TODO: create folder if it doesn't exist

            foreach( var entity in fgd.entityTypes ) {
                if (entity.objScale > 0)
                {
                    GameObject export = entity.meshOverride != null ? entity.meshOverride : entity.entityPrefab;
                    ObjExport.SaveObjFile( folder + entity.className + ".obj", new GameObject[] {export}, Vector3.one * entity.objScale, true, true, true);
                }
            }
        }

    }
}