using UnityEngine;

public class DayAndNight : MonoBehaviour
{

    // Assets/SimpleSky/Materials/SimpleSky.mat
    // Assets/Real Stars Skybox/StarSkybox04/StarSkybox04.mat
   [Header("Materiales del Skybox")]
    public Material simpleSkyMaterial;
    public Material realStarsMaterial;
    public Material sunsetSkyMaterial;
    public Material superNovaSkyMaterial;

    // Función 1: Cambia al cielo simple
    public void SetForestDay()
    {
        if (simpleSkyMaterial != null)
        {
            RenderSettings.skybox = simpleSkyMaterial;
            DynamicGI.UpdateEnvironment(); // Actualiza la iluminación global
            Debug.Log("Cielo cambiado a: SimpleSky");
        }
    }

    // Función 2: Cambia al cielo de estrellas
    public void SetDarkNight()
    {
        if (realStarsMaterial != null)
        {
            RenderSettings.skybox = realStarsMaterial;
            DynamicGI.UpdateEnvironment();
            Debug.Log("Cielo cambiado a: Real Stars");
        }
    }

    // Función 3: Cambia el cielo a atardecer
    public void SetBeachSunset()
    {
        if (sunsetSkyMaterial != null)
        {
            RenderSettings.skybox = sunsetSkyMaterial;
            DynamicGI.UpdateEnvironment();
            Debug.Log("Cielo cambiado a: Atardecer");
        }
    }

    // Función 4: Cambia el cielo a una supernova
    public void SetWhiteSuperNova()
    {
        if (superNovaSkyMaterial != null)
        {
            RenderSettings.skybox = superNovaSkyMaterial;
            DynamicGI.UpdateEnvironment();
            Debug.Log("Cielo cambiado a: Supernova");
        }
    }
}