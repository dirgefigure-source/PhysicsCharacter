using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;


public class HierarchyExporter
{

    [MenuItem("Tools/Export Hierarchy Detail JSON")]
    public static void ExportHierarchy()
    {

        GameObject[] objects =
            Object.FindObjectsOfType<GameObject>();


        ExportRoot root =
            new ExportRoot();


        root.objects =
            new List<GameObjectData>();


        foreach(GameObject obj in objects)
        {

            GameObjectData go =
                new GameObjectData();


            go.name = obj.name;

            go.active =
                obj.activeSelf;


            if(obj.transform.parent != null)
            {
                go.parent =
                    obj.transform.parent.name;
            }
            else
            {
                go.parent = "";
            }


            go.components =
                new List<ComponentData>();



            Component[] comps =
                obj.GetComponents<Component>();


            foreach(Component comp in comps)
            {

                if(comp == null)
                    continue;


                ComponentData component =
                    new ComponentData();


                component.type =
                    comp.GetType().FullName;


                component.properties =
                    new List<PropertyData>();



                SerializedObject so =
                    new SerializedObject(comp);



                SerializedProperty prop =
                    so.GetIterator();



                // 跳过脚本对象本身
                bool enterChildren = true;


                while(prop.NextVisible(enterChildren))
                {

                    enterChildren = false;


                    PropertyData pd =
                        new PropertyData();


                    pd.name =
                        prop.name;


                    pd.value =
                        GetPropertyValue(prop);



                    component.properties.Add(pd);


                    // 数组展开
                    if(prop.isArray &&
                       prop.propertyType ==
                       SerializedPropertyType.Generic)
                    {

                        ExportArray(
                            prop,
                            component.properties
                        );

                    }

                }



                go.components.Add(component);

            }


            root.objects.Add(go);

        }



        string json =
            JsonUtility.ToJson(
                root,
                true
            );


        string path =
            Application.dataPath +
            "/HierarchyDetail.json";


        File.WriteAllText(
            path,
            json
        );


        AssetDatabase.Refresh();


        Debug.Log(
            "Hierarchy导出完成:\n"
            + path
        );

    }





    // =============================
    // 属性读取
    // =============================

    static string GetPropertyValue(
        SerializedProperty prop)
    {

        switch(prop.propertyType)
        {

            case SerializedPropertyType.Integer:

                return prop.intValue.ToString();



            case SerializedPropertyType.Boolean:

                return prop.boolValue.ToString();



            case SerializedPropertyType.Float:

                return prop.floatValue.ToString();



            case SerializedPropertyType.String:

                return prop.stringValue;



            case SerializedPropertyType.Vector2:

                return prop.vector2Value.ToString();



            case SerializedPropertyType.Vector3:

                return prop.vector3Value.ToString();



            case SerializedPropertyType.Vector4:

                return prop.vector4Value.ToString();



            case SerializedPropertyType.Quaternion:

                return prop.quaternionValue.ToString();



            case SerializedPropertyType.Color:

                return prop.colorValue.ToString();



            case SerializedPropertyType.Rect:

                return prop.rectValue.ToString();



            case SerializedPropertyType.Bounds:

                return prop.boundsValue.ToString();



            case SerializedPropertyType.Enum:

                return prop.enumDisplayNames[
                    prop.enumValueIndex
                ];



            case SerializedPropertyType.ObjectReference:

                if(prop.objectReferenceValue != null)
                {

                    string path =
                        AssetDatabase.GetAssetPath(
                            prop.objectReferenceValue
                        );


                    if(string.IsNullOrEmpty(path))
                    {
                        return prop.objectReferenceValue.name;
                    }


                    return path;

                }


                return "null";



            case SerializedPropertyType.AnimationCurve:

                return "AnimationCurve";



            case SerializedPropertyType.ExposedReference:

                return "ExposedReference";



            default:

                return
                    "["+
                    prop.propertyType+
                    "]";

        }

    }





    // =============================
    // 数组/List展开
    // =============================

    static void ExportArray(
        SerializedProperty array,
        List<PropertyData> list)
    {

        for(int i=0;
            i<array.arraySize;
            i++)
        {

            SerializedProperty element =
                array.GetArrayElementAtIndex(i);



            PropertyData pd =
                new PropertyData();


            pd.name =
                array.name+
                "["+
                i+
                "]";


            pd.value =
                GetPropertyValue(element);



            list.Add(pd);


        }

    }





    // =============================
    // 数据结构
    // =============================


    [System.Serializable]
    public class ExportRoot
    {
        public List<GameObjectData> objects;
    }




    [System.Serializable]
    public class GameObjectData
    {

        public string name;

        public bool active;

        public string parent;


        public List<ComponentData> components;

    }




    [System.Serializable]
    public class ComponentData
    {

        public string type;


        public List<PropertyData> properties;

    }




    [System.Serializable]
    public class PropertyData
    {

        public string name;


        public string value;

    }

}