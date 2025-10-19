using System;
using UnityEngine;

public class AutoRotate : MonoBehaviour
{
    public Vector3 rotationSpeed;
    

    void Update()
    {
        transform.localRotation *= Quaternion.Euler(rotationSpeed * Time.deltaTime);
    }
}
