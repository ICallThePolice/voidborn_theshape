using System;

// Атрибут Serializable нужен, чтобы Unity мог сохранять наследников этого класса
// внутри ScriptableObject (нашего BiomeDefinition).
[Serializable]
public abstract class BiomeModifier
{
    // В будущем здесь могут быть общие для всех модификаторов свойства,
    // например, public bool enabled = true;
    
    // Каждый наследник должен будет реализовать свою логику,
    // но на уровне C# нам пока не нужно определять здесь методы.
    // Вся магия будет происходить в Compute Shader.
}