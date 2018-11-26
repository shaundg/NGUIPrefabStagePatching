# NGUI Prefab Stage Editing 
Hack to workaround Unity 2018.3.xb PrefabStage editing of Prefabs using NGUI UI

Currently, in Unity 2018.3.0xb (beta), you're unable to properly edit Prefabs which contain NGUI-based UI (UIRect-based components).
This current work around leverages Harmony (https://github.com/pardeike/Harmony) to inject methods which patch the Prefab Stage 
to include the UIRoot setup and display UI based on NGUI. 

Additionally, it patches the UIDrawCall Create method to forward UIDrawCalls into the open PrefabStage Scene.

Feel free to reach out if you have questions - twitter @shaunpeoples

Thanks to Artyom Zuev of Brace Yourself Games: https://braceyourselfgames.com/about/
