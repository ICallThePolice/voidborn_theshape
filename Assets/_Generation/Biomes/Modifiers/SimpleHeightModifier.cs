using System;
using UnityEngine;

// Мы не используем здесь CreateAssetMenu, так как это не отдельный ассет,
// а класс, который будет жить внутри списка в BiomeDefinition.
[Serializable]
public class SimpleHeightModifier : BiomeModifier
{
    [Tooltip("На сколько юнитов изменить высоту ландшафта. Положительное значение - горы, отрицательное - низины.")]
    public float heightOffset = 0f;
}