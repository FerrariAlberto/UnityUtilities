// Author: Alberto Ferrari
// Basic atlas implementation, select 4 meshes and press "Generate Atlas"
// supports only four at the time, will create duplicates meshes with updated UVs 

#if UNITY_EDITOR

using System.Collections;
using System.Threading;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;

public class AutoAtlasEditor : EditorWindow
{
    public class ThreadData
    {
        public int start;
        public int end;
        public ThreadData(int s, int e)
        {
            start = s;
            end = e;
        }
    }

    private static Color[] texColors;
    private static Color[] newColors;
    private static int w;
    private static float ratioX;
    private static float ratioY;
    private static int w2;
    private static int finishCount;
    private static Mutex mutex;

    List<Texture2D> allTexture;
    GameObject[] selectedObjects;
    List<string> texturesNames;
    string[] atlasNames;
    public Texture2D[] atlas = new Texture2D[1];
    Material generatedMaterial;
    string path = "Assets";

    int counter;

    public string atlasName = "Atlas";
    
    bool fouldout, fouldoutS;

    int selected = 3;
    string[] options = new string[]{"256", "512", "1024", "2048", "4096", "8192"};
    int[] width_height = new int[] { 256, 512, 1024, 2048, 4096, 8192 };

    bool showTextures;
    Shader shader;
    Object matSource;


    [MenuItem("Tools/AlbertoFerrari/AutoAtlas")]
    public static void ShowWindow()
    {
        EditorWindow.GetWindow(typeof(AutoAtlasEditor));
    }

    void OnGUI()
    {
        GUILayout.Label("Generate atlas from four objects", EditorStyles.boldLabel);


        if (GUILayout.Button("Generate Atlas"))
        {
            if (Selection.gameObjects == null || Selection.gameObjects.Length == 0)
            {
                EditorUtility.DisplayDialog("Generate Atlas", "You need to select at least one object", "ok");
            }
            else if (Selection.gameObjects.Length > 4)
            {
                EditorUtility.DisplayDialog("Generate Atlas", "Four objects maximum", "ok");
            }
            else
            {
                for (int i = 0; i < Selection.gameObjects.Length; i++)
                {
                    if (Selection.gameObjects[i].GetComponent<MeshFilter>() == null)
                    {
                        EditorUtility.DisplayDialog("Generate Atlas", "You must select objects with a valid meshes.", "ok");
                        break;
                    }
                    if (Selection.gameObjects[i].GetComponent<Renderer>() == null)
                    {
                        EditorUtility.DisplayDialog("Generate Atlas", "You must select objects with a valid material.", "ok");
                        break;
                    }
                    else if (Selection.gameObjects[i].GetComponent<Renderer>().sharedMaterial.shader != Selection.gameObjects[0].GetComponent<Renderer>().sharedMaterial.shader)
                    {
                        EditorUtility.DisplayDialog("Generate Atlas", "Objects must share the same shader.", "ok");
                        break;
                    }
                    else
                    {
                        GenerateAtlas();
                        break;
                    }
                }
            }

        }

        if (atlas[0] != null)
        {
            fouldout = EditorGUILayout.Foldout(fouldout, "Generated Assets");
            if (fouldout)
            {
                ScriptableObject target = this;
                SerializedObject so = new SerializedObject(target);
                SerializedProperty properties = so.FindProperty("atlas");
                EditorGUILayout.PropertyField(properties, true);

                matSource = EditorGUILayout.ObjectField("Atlas Material: ", generatedMaterial, typeof(Material));

                if (GUILayout.Button("Fix Normal"))
                {
                    if (Selection.activeObject is Texture2D && EditorUtility.DisplayDialog("Fix Normal", "Are you sure you want to fix " + Selection.activeObject.name, "Yes", "No"))
                    {
                        Texture2D tex = (Texture2D)AssetDatabase.LoadAssetAtPath(AssetDatabase.GetAssetPath(Selection.activeObject), typeof(Texture2D));
                        if (tex.format == TextureFormat.RGBA32 || tex.format == TextureFormat.ARGB32 || tex.format == TextureFormat.Alpha8 || tex.format == TextureFormat.RGB24)
                        {
                            FixNormal(tex);
                        }
                        else
                        {
                            if (EditorUtility.DisplayDialog("Fix Normal", "Texture format needs to be RGBA32(suggested), ARGB32, RGB24 or Alpha8", "ok")) { }
                        }

                    }
                    else
                    {
                        if (EditorUtility.DisplayDialog("Fix Normal", "You need to select a texture", "ok")) { }
                    }
                }

                EditorGUILayout.HelpBox("To fix the normal map, change the format to RGBA32 and set it as normal then press Fix Normal", MessageType.Info);
            }
        }

        fouldoutS = EditorGUILayout.Foldout(fouldoutS, "Settings");
        if (fouldoutS)
        {

            path = EditorGUILayout.TextField("Current Path:", path);
            if (GUILayout.Button("Change Path"))
            {
                path = EditorUtility.OpenFolderPanel("aa", "", "");
                path = "Assets" + path.Remove(0, Application.dataPath.Length);
            }

            atlasName = EditorGUILayout.TextField("Atlas Name:", atlasName);

            selected = EditorGUILayout.Popup("Atlas Size:", selected, options);


        }
        
    }

    public void GenerateAtlas()
    {
        PopulateArray(Selection.gameObjects);

        //duplicates selected objects
        GameObject[] objects = new GameObject[selectedObjects.Length];
        for (int i = 0; i < selectedObjects.Length; i++)
        {
            objects[i] = Instantiate(selectedObjects[i], selectedObjects[i].transform.position, selectedObjects[i].transform.rotation);
            objects[i].name = selectedObjects[i].name + "_" + atlasName;
            selectedObjects[i].SetActive(false);
        }

        //creats a new material from a copy of the first object material. 
        Material newMaterial = Material.Instantiate(objects[0].GetComponent<MeshRenderer>().sharedMaterial);

        //creates a new array of textures(atlases), one for each unique texture in the material
        atlas = new Texture2D[allTexture.Count / selectedObjects.Length];
        atlasNames = new string[atlas.Length];

        for (int i = 0; i < atlas.Length; i++)
        {
            atlas[i] = new Texture2D(width_height[selected], width_height[selected], TextureFormat.RGBA32, false);
            atlasNames[i] = texturesNames[i];
        }

        //copies old textures into the nuew atlases with offsets
        for (int j = 0; j < atlasNames.Length; j++)
        {
            counter = 0;

            for (int i = 0; i < allTexture.Count; i++)
            {
                if (texturesNames[i] == atlasNames[j])
                {
                    switch (counter)
                    {
                        case 0:
                            Graphics.CopyTexture(allTexture[i], 0, allTexture[i].loadedMipmapLevel, 0, 0, allTexture[i].width, allTexture[i].height, atlas[j], 0, allTexture[i].loadedMipmapLevel, 0 * (int)(atlas[j].width * .5f), 0 * (int)(atlas[j].height * .5f));
                            break;
                        case 1:
                            Graphics.CopyTexture(allTexture[i], 0, allTexture[i].loadedMipmapLevel, 0, 0, allTexture[i].width, allTexture[i].height, atlas[j], 0, allTexture[i].loadedMipmapLevel, 1 * (int)(atlas[j].width * .5f), 0 * (int)(atlas[j].height * .5f));
                            break;
                        case 2:
                            Graphics.CopyTexture(allTexture[i], 0, allTexture[i].loadedMipmapLevel, 0, 0, allTexture[i].width, allTexture[i].height, atlas[j], 0, allTexture[i].loadedMipmapLevel, 0 * (int)(atlas[j].width * .5f), 1 * (int)(atlas[j].height * .5f));
                            break;
                        case 3:
                            Graphics.CopyTexture(allTexture[i], 0, allTexture[i].loadedMipmapLevel, 0, 0, allTexture[i].width, allTexture[i].height, atlas[j], 0, allTexture[i].loadedMipmapLevel, 1 * (int)(atlas[j].width * .5f), 1 * (int)(atlas[j].height * .5f));
                            break;
                    }
                    counter++;
                }
            }

            SaveAtlas(atlas[j], j);
        }
        

        //creates new meshes and changes UVs to metch new textures
        for (int j = 0; j < objects.Length; j++)
        {
            Mesh mesh = objects[j].GetComponent<MeshFilter>().sharedMesh;
            Mesh newmesh = new Mesh();
            newmesh.vertices = mesh.vertices;
            newmesh.triangles = mesh.triangles;
            newmesh.uv = mesh.uv;
            newmesh.normals = mesh.normals;
            newmesh.colors = mesh.colors;
            newmesh.tangents = mesh.tangents;

            Vector2[] uvs = new Vector2[newmesh.uv.Length];

            for (int i = 0; i < uvs.Length; i++)
            {
                switch (j)
                {
                    case (0):
                        uvs[i] = objects[j].GetComponent<MeshFilter>().sharedMesh.uv[i] * 0.5f + new Vector2(0, 0);
                        break;
                    case (1):
                        uvs[i] = objects[j].GetComponent<MeshFilter>().sharedMesh.uv[i] * 0.5f + new Vector2(0.5f, 0);
                        break;
                    case (2):
                        uvs[i] = objects[j].GetComponent<MeshFilter>().sharedMesh.uv[i] * 0.5f + new Vector2(0, 0.5f);
                        break;
                    case (3):
                        uvs[i] = objects[j].GetComponent<MeshFilter>().sharedMesh.uv[i] * 0.5f + new Vector2(0.5f, 0.5f);
                        break;
                }
            }

            objects[j].GetComponent<MeshFilter>().sharedMesh = newmesh;
            objects[j].GetComponent<MeshFilter>().sharedMesh.uv = uvs;
            objects[j].GetComponent<MeshRenderer>().sharedMaterial = newMaterial;


            AssetDatabase.CreateAsset(newmesh, path + "/" + objects[j] + ".asset");
        }

        SaveMaterial(newMaterial);
        showTextures = true;
    }

    //saves generated atlas to path
    void SaveAtlas(Texture2D texture, int textureNumber)
    {
        byte[] bytes = texture.EncodeToPNG();

        FileStream fileSave = new FileStream(Application.dataPath + path.Remove(0,6) + "/" + atlasName + atlasNames[textureNumber] + ".png", FileMode.Create);

        BinaryWriter binary = new BinaryWriter(fileSave);
        binary.Write(bytes);
        fileSave.Close();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        atlas[textureNumber] = (Texture2D)AssetDatabase.LoadAssetAtPath(path + "/" + atlasName + atlasNames[textureNumber] + ".png", typeof(Texture2D));
    }

    //saves generated material to path and populates it with new textures
    void SaveMaterial(Material material)
    {
        AssetDatabase.CreateAsset(material, path + "/" + atlasName + "_MAT.mat");
        AssetDatabase.SaveAssets();

        Material mat = (Material)AssetDatabase.LoadAssetAtPath(path + "/" + atlasName + "_MAT.mat", typeof(Material));

        for (int i = 0; i < atlas.Length; i++)
        {
           mat.SetTexture(atlasNames[i], (Texture2D)AssetDatabase.LoadAssetAtPath(path + "/" + atlasName + atlasNames[i] + ".png", typeof(Texture2D)));
        }

        generatedMaterial = mat;

        AssetDatabase.Refresh();
    }

    //switch rgb channels of textures
    public void FixNormal(Texture2D sourceNormal)
    {
        Color[] sourceColor = sourceNormal.GetPixels();
        Color[] resultingColor = new Color[sourceColor.Length];
        
        for (int i = 0; i < sourceColor.Length; i++)
        {
            resultingColor[i] = new Color(sourceColor[i].g, sourceColor[i].b, sourceColor[i].r);
        }
        
        sourceNormal.SetPixels(resultingColor);
        sourceNormal.Apply();
    }
    
    //finds all needed objects from selected gameObjects
    public void PopulateArray(GameObject[] selected)
    {
        selectedObjects = new GameObject[selected.Length];
        allTexture = new List<Texture2D>();
        texturesNames = new List<string>();
        for (int i = 0; i < selected.Length; i++)
        {
            selectedObjects[i] = selected[i];
        }

        for (int j = 0; j < selectedObjects.Length; j++)
        {
            Shader shader = selectedObjects[j].GetComponent<MeshRenderer>().sharedMaterial.shader;
            for (int i = 0; i < ShaderUtil.GetPropertyCount(shader); i++)
            {
                if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
                {
                    Texture2D texture = (Texture2D)selectedObjects[j].GetComponent<MeshRenderer>().sharedMaterial.GetTexture(ShaderUtil.GetPropertyName(shader, i));
                    if (texture != null)
                    {
                        texturesNames.Add(ShaderUtil.GetPropertyName(shader, i));
                        allTexture.Add(texture);
                    }
                }
            }
        }

        for (int i = 0; i < allTexture.Count; i++)
        {
                TextureFormat format = TextureFormat.RGBA32;
                allTexture[i] = ChangeTextureFormat(allTexture[i], format, .5f);
        }
    }

    public Texture2D ChangeTextureFormat(Texture2D currentTexture, TextureFormat format, float scale)
    {
        Texture2D newTex = new Texture2D(currentTexture.width, currentTexture.height, format, false);
        newTex.SetPixels(currentTexture.GetPixels());
        newTex.Apply();

        if(scale != 1)
            ScaleTexture(newTex, (int)(width_height[selected] * scale), (int)(width_height[selected] * scale));
        
        return newTex;
    }

    public void ScaleTexture(Texture2D tex, int newWidth, int newHeight)
    {
        texColors = tex.GetPixels();
        newColors = new Color[newWidth * newHeight];

        ratioX = ((float)tex.width) / newWidth;
        ratioY = ((float)tex.height) / newHeight;

        w = tex.width;
        w2 = newWidth;

        var cores = Mathf.Min(SystemInfo.processorCount, newHeight);
        var slice = newHeight / cores;

        finishCount = 0;
        if (mutex == null)
        {
            mutex = new Mutex(false);
        }
        if (cores > 1)
        {
            int i = 0;
            ThreadData threadData;
            for (i = 0; i < cores - 1; i++)
            {
                threadData = new ThreadData(slice * i, slice * (i + 1));
                ParameterizedThreadStart ts = new ParameterizedThreadStart(Scale);
                Thread thread = new Thread(ts);
                thread.Start(threadData);
            }
            threadData = new ThreadData(slice * i, newHeight);

            Scale(threadData);

            while (finishCount < cores)
            {
                Thread.Sleep(1);
            }
        }
        else
        {
            ThreadData threadData = new ThreadData(0, newHeight);
            Scale(threadData);
        }

        tex.Resize(newWidth, newHeight);
        tex.SetPixels(newColors);
        tex.Apply();

        texColors = null;
        newColors = null;
    }

    public static void Scale(System.Object obj)
    {
        ThreadData threadData = (ThreadData)obj;
        for (var y = threadData.start; y < threadData.end; y++)
        {
            var thisY = (int)(ratioY * y) * w;
            var yw = y * w2;
            for (var x = 0; x < w2; x++)
            {
                newColors[yw + x] = texColors[(int)(thisY + ratioX * x)];
            }
        }

        mutex.WaitOne();
        finishCount++;
        mutex.ReleaseMutex();
    }
}

#endif
