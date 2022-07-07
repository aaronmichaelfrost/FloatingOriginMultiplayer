Thank you for using twoloop's Floating Origin Ultimate!

Please read the "FOU - Getting Started" pdf for a guide on how (and how not) to use this tool.

Join Discord if you need help. Someone might be able to help you. - https://discord.gg/fcShgTSjJX

Contributions are appreciated. Also message me (twoloop) if you run into any bugs!

- - - - - - - - - - - - - - - - - - - - - - -

SINGLE PLAYER 

- Put FloatingOrigin component on gameobject in your scene
- Set the focus transform to your player
- Set the precision mode to float

- - - - - - - - - - - - - - - - - - - - - - -

MULTIPLAYER

Try out the Mirror example scene.

First install the latest version of Mirror: 
https://assetstore.unity.com/packages/tools/network/mirror-129321

- - - - - - - - - - - - - - - - - - - - - - -

FAQ:

How do I update stored world space positions?

Create a new function to accept the translation updates:

void OnShiftDetected(Vector3 newWorldOffset, Vector3 translation)
{
    // Do whatever you need with translation, usually add it to each world space Vector3 field in your class

}


Then register that function you just created in a new Awake() function, like so:

void Awake()
{
    twoloop.FloatingOrigin.OnOriginShifted.AddListener(OnShiftDetected);
}
