using UnityEngine;

public class DayAndNight : MonoBehaviour
{

    // Assets/SimpleSky/Materials/SimpleSky.mat
    // Assets/Real Stars Skybox/StarSkybox04/StarSkybox04.mat
   [Header("Materiales del Skybox")]
    public Material simpleSkyMaterial;
    public Material realStarsMaterial;

    // Función 1: Cambia al cielo simple
    public void SetDay()
    {
        if (simpleSkyMaterial != null)
        {
            RenderSettings.skybox = simpleSkyMaterial;
            DynamicGI.UpdateEnvironment(); // Actualiza la iluminación global
            Debug.Log("Cielo cambiado a: SimpleSky");
        }
    }

    // Función 2: Cambia al cielo de estrellas
    public void SetNight()
    {
        if (realStarsMaterial != null)
        {
            RenderSettings.skybox = realStarsMaterial;
            DynamicGI.UpdateEnvironment();
            Debug.Log("Cielo cambiado a: Real Stars");
        }
    }
}