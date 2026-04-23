//BORRAR EL SCRIPT CUANDO SE IMPLEMENTE EL CAMBIO DIA/NOCHE EN EL TRANSPORTE HACIA ENTORNOS
using UnityEngine;

public class SkyboxTest : MonoBehaviour
{
    public DayAndNight dayAndNight;

    void Start()
    {
        if (dayAndNight != null)
        {
            // Llamamos a una de las funciones para probar
            dayAndNight.SetDay();
        }
        else
        {
            Debug.LogWarning("No has asignado el DayAndNight al script de prueba.");
        }
    }
}