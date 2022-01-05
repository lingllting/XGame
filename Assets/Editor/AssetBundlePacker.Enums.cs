using System;

public enum eAssetType
{
    NONE = 0,
    TEXT,
    SCRIPT,
    AUDIO,
    ANIM,
    SHADER,
    TEXTURE,
    CONTROLLER,
    MATERIAL,
    FBX,
    PREFAB,
    SCENE,
    Count,
}

public enum eBundleType
{
    None = 0,
    Scene,
    SceneRes,
    Common,
    Animation,
    Model,
    Atlas,
    Texture,
    Shader,
    Audio,
    Text,
    Prefab,
}

[Flags]
public enum eFilterType
{
    None = 0,
    Shader = 1,
    Config = 2,
    Dependence = 4,
    Directory = 8,
}

public enum eMerageType
{
    None,
    IdenticalInDegree,
    Actoz,
    Cloth,
    Weapon,
    Monster,
    Wing,
    Horse,
    Magic,
    Scene,
    SceneEffect,
    CodeResource,
    LODEffect,
    Other,
}

public enum eStatisBundleType
{
    NONE = 0,
    COMMON,
    CONFIG,
    SCRIPT,
    SHADER,
    SCENE,
    SCENE_RES,
    PREFAB_CHARACTERS,
    PREFAB_EFFECT,
    PREFAB_UGUI,
    PREFAB_HORSE,
    PREFAB_WEAPON,
    PREFAB,
    EFFECT,
    AUDIO,
    MEDIA_MODEL,
    MEDIA,
    FONT,
    ATLAS,
    UI,
    Count,
}
