using UnityEngine;
using System.Collections;

public class mouseRotator : MonoBehaviour {

	public float sensitivity = 3f;

	Vector3 lastMousePos = new Vector3();
	float rotX = 0;
	float rotY = 0;

    bool paused = false;

    public void pause()  //this is used to keep the mouseRotator from rotating during a button press,.
    {
        paused = true;
    }


	void Update () 
	{
        if (Input.GetKeyDown(KeyCode.Escape))
            quitApp();

        if (Input.GetMouseButtonUp(0))
            paused = false;

        if (paused)
            return;

		if (Input.GetMouseButton (0)) 
		{
			Vector3 mouseDiff =  (Input.mousePosition - lastMousePos) ;
			rotX -= mouseDiff.x * sensitivity * .01f; //these are purpose
			rotY += mouseDiff.y * sensitivity * .01f;
			transform.rotation = Quaternion.Euler(rotY, rotX, 0f);
		}

		lastMousePos = Input.mousePosition;
	}

    public void quitApp()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
         Application.Quit();
#endif
    }
}
