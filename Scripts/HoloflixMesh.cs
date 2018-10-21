//David Lycan - Define UNITY_VERSION_PRE_5_2
#if UNITY_5_0 || UNITY_5_0_1 || UNITY_5_0_2 || UNITY_5_0_3 || UNITY_5_0_4 || UNITY_5_1 || UNITY_5_1_1 || UNITY_5_1_2 || UNITY_5_1_3 || UNITY_5_1_4 || UNITY_5_1_5
	#define UNITY_VERSION_PRE_5_2
#endif

using UnityEngine;
using System.Collections;
using System.Collections.Generic;

//creates a sufficiently dense mesh for the displacement

[ExecuteInEditMode]
public class HoloflixMesh : MonoBehaviour {

	public int meshResX = 200;
	public int meshResY = 200;
	[Tooltip ("The resolution of the entire movie texture.")]
	public Vector2 imageRes;
	[Tooltip ("The upper left pixel x/y of the color portion of the movie.")]
	public Vector2 rgbCoord;
	[Tooltip ("The pixel width/height of the color portion of the movie.")]
	public Vector2 rgbDim;

	//public Material depthMovieMaterial;

	public float depth = 1f;
	public float persp = 1f;
	public float size = 1f;

	//public UnityEngine.UI.Slider depthSlider;
	//public UnityEngine.UI.Slider perspSlider;
	//public UnityEngine.UI.Slider sizeSlider;

	protected MovieTexture movie = null;
	protected AudioSource audioSrc = null;
	
	//David Lycan - Previous dimensions for material input
	private int graphicsShaderLevel = 0;
	private Vector4 previousDims = Vector4.zero;

	//these are necessary because there is no way (that i know of or could find) to detect if a shader is using a fallback.
	//so we handle falling back ourselves.
	//for example, if we include the fallbacks in the shader itself, shader.isSupported will return true (since its fallback is working)
	//but this leaves us no way to know that the fallback is being used. Among several possible hacks/workarounds, this is the one I found most stable.
	//We just detect failed shader.isSupported, and manage the falling back ourselves.
	Shader currentShader;

    /*
    public Shader holovidShader;
	public Shader particleShader;
	public Shader particleAdditiveShader;
	public Shader particleCutoutShader;
	public Shader particleFallbackShader;
	public Shader particleAdditiveFallbackShader;
	public Shader particleCutoutFallbackShader;
    */

	//internal mesh generating stuff.  These are member variables to prevent tons of GC allocation
	List<Vector3> verts = null;
	int[] triangles;
	List<Vector2> uvs = null;
	List<Vector2> uv2s = null;
	List<Vector3> normals = null;

	Vector3 bottomLeft = new Vector3 ();  //internal
	Vector3 topRight = new Vector3 ();
	Vector3 bottomRight = new Vector3 ();

	Vector3 cellLeft = new Vector3 (); //internal
	Vector3 cellRight = new Vector3 (); 

	int _currentResX = 0; //these help us ensure that we are always building meshes of the appropriate size even if the dev or user makes changes to our dims 
	int _currentResY = 0;
	Shader _lastShader = null;

	void Start()
	{
		checkShaders (); //just for safety.
		
		if (Application.isPlaying)
		{
			if (!movie) 
			{
				Renderer r = GetComponent<Renderer>();
				if (r)
					movie = (MovieTexture)r.material.mainTexture;
			}

			if (movie) 
			{
				movie.Play ();
				audioSrc = GetComponent<AudioSource> ();
				audioSrc.clip = movie.audioClip;
				audioSrc.Play ();
			}
		}

		sliderChanged (); //start with whatever values the sliders have
	}


	public void play()
	{
		if (movie)
			movie.Play();
		if (audioSrc)
			audioSrc.Play ();

	}
	public void stop()
	{
		if (movie) 
		{
			movie.Stop (); //this leaves it at the current frame. when what it should really do is reset to frame 1
			StartCoroutine("stopping");
			audioSrc.Stop ();
		}
	}
	public void pause()
	{
		if (movie)
			movie.Pause();
		if (audioSrc)
			audioSrc.Pause ();
	}
		

	IEnumerator stopping()
	{
		yield return new WaitForSeconds (.1f);
		movie.Play ();
		yield return new WaitForSeconds (.1f);
		movie.Pause ();//.should now be at frame 1

	}
		
	public void sliderChanged()
	{
		GetComponent<Renderer>().sharedMaterial.SetFloat("_Displacement", depth);//Slider.value);
		GetComponent<Renderer>().sharedMaterial.SetFloat("_ForcedPerspective", persp);//Slider.value);
		GetComponent<Renderer>().sharedMaterial.SetFloat ("_ParticleSize", size);//Slider.value);
	}

	void updateSettings()
	{
		meshResX = Mathf.Clamp (meshResX, 1, 255);
		meshResY = Mathf.Clamp (meshResY, 1, 254);

		while (meshResX + meshResY > 253)//this will create too many verts 
		{ 
			Debug.Log ("Reducing the mesh resolution, too many verts would be created."); //remember unity can only have 64k verts on a mesh
			meshResX--;
		}

		//populate all the arrays outside of the mesh update loop to avoid tons of GC allocation
		int vertCount = (meshResX + 1) * (meshResY + 1); //+1 is inclusive since we need verts at the end of the count to complete the quads
		if (graphicsShaderLevel == 1)
			vertCount = vertCount * 4; //we need to account for our own verts here, since our GPU does not support the needed shader

		int triangleCount =  meshResX * meshResY * 6;
		if (graphicsShaderLevel == 2)
			triangleCount = meshResX * meshResY * 3;

		verts = new List<Vector3> (); 
		uvs = new List<Vector2> ();
		uv2s = new List<Vector2> ();
		normals = new List<Vector3> ();
		for (int v = 0; v < vertCount; v++) //populate the list.. verts are required in this format due to the way the mesh takes in verts as an argument
		{
			verts.Add (Vector3.zero);
			uvs.Add (new Vector2());
			uv2s.Add (new Vector2());
			normals.Add (Vector3.zero);
		}
		triangles = new int[triangleCount];

		_currentResX = meshResX;
		_currentResY = meshResY;
	}

	void OnValidate()
	{
		checkShaders ();
		updateSettings ();
	}

	void checkShaders()
	{
		//Detect whether current Graphics Shader Level supports Geometry shaders
		//manually set the fallbacks so we can detect that the fallback occured and adjust our geometry accordingly
		Material m = GetComponent<Renderer>().sharedMaterial;
		if (!m) 
		{
			return;
		}
		currentShader = m.shader;
		if (currentShader == _lastShader) //nothing to do here.
			return;

		/*
        if (currentShader == particleShader && !particleShader.isSupported) 
		{
			m.shader = particleFallbackShader;
			currentShader = particleFallbackShader;
		}
		if (currentShader == particleAdditiveShader && !particleAdditiveShader.isSupported) 
		{
			m.shader = particleAdditiveFallbackShader;
			currentShader = particleAdditiveFallbackShader;
		}
		if (currentShader == particleCutoutShader && !particleCutoutShader.isSupported) 
		{
			m.shader = particleCutoutFallbackShader;
			currentShader = particleCutoutFallbackShader;
		}

		//if even the fallback is bad, then fall back to the default holovid shader.
		if (currentShader == particleFallbackShader && !particleFallbackShader.isSupported) 
		{
			m.shader = holovidShader;
			currentShader = holovidShader;
		}
		if (currentShader == particleAdditiveFallbackShader && !particleAdditiveFallbackShader.isSupported) 
		{
			m.shader = holovidShader;
			currentShader = holovidShader;
		}
		if (currentShader == particleCutoutFallbackShader && !particleCutoutFallbackShader.isSupported) 
		{
			m.shader = holovidShader;
			currentShader = holovidShader;
		}

		//shader level 2 means we can support everything.  Probably this machine is using >= DX11 This will mean the GPU will cut the mesh as needed.
		//shader level 1 means the GPU can't support cutting up the mesh, so we have to do the cutting in C# and the shader only pushes the verts around based on camera view.
		//shader level 0 means we are just trying to use the simplest method, which is just vert displacement based on the depth movie texture values and material settings, with no mesh cutting

		if (currentShader == particleShader || currentShader == particleAdditiveShader || currentShader == particleCutoutShader) 
			graphicsShaderLevel = 2;
		else if (currentShader == particleFallbackShader || currentShader == particleAdditiveFallbackShader || currentShader == particleCutoutFallbackShader) 
			graphicsShaderLevel = 1;
		else
			graphicsShaderLevel = 0;
        */

		//Unity doesn't seem to set keyword shader values properly when switching materials
		//meaning that the GUI may not match the shader's value.. the lines below forces them to be updated coherently
		//for more info: https://docs.unity3d.com/ScriptReference/MaterialPropertyDrawer.html
		m.EnableKeyword ("ENABLE_SOFTSLICING"); 
		m.SetFloat ("_softSlicingToggle", 1f);


		updateSettings ();
		_lastShader = currentShader;
	}
	
	void Update()
	{
		sliderChanged();

		checkShaders ();

		meshResX = Mathf.Clamp (meshResX, 1, 255);
		meshResY = Mathf.Clamp (meshResY, 1, 254); //maximum vert count limit
		
		if (meshResX != _currentResX || meshResY != _currentResY)
			updateSettings ();


		if (imageRes.x < 1)
			imageRes.x = 1;
		if (imageRes.y < 1)
			imageRes.y = 1;
		if (rgbDim.x < 1)
			rgbDim.x = 1;
		if (rgbDim.y < 1)
			rgbDim.y = 1;

		//David Lycan - Update the dimensions in the movie's material if necessary
		Vector4 newDims = new Vector4( 1.07333f * rgbDim.y / imageRes.x, 1.09f * rgbDim.y / imageRes.y, 1f, 1f);
		
		if (previousDims != newDims)
		{
			if (graphicsShaderLevel > 0)
			{
				//depthMovieMaterial.SetVector("_Dims", newDims);
			}
			
			previousDims = newDims;
		}
		
		
		generateHolovidMesh (gameObject, 
			new Vector2 (-.5f, -.5f), //position of the mesh
			new Vector2 (rgbDim.x / rgbDim.y, 1f), //size of the mesh
			new Vector2(rgbCoord.x/imageRes.x, rgbCoord.y/imageRes.y), // uv pos 
			new Vector2 (rgbDim.x/imageRes.x, rgbDim.y/imageRes.y)); //uv dims - send whole and let the shader do the work here.
	}




	void generateHolovidMesh(GameObject g, Vector2 pos, Vector2 dims, Vector2 UVpos, Vector2 UVdims)
	{


        //vert index
		int v = 0;
        Vector2 uv = new Vector2();
		for (int r = 0; r <= meshResY; r++)
		{
			//lerp between the top left and bottom left, then lerp between the top right and bottom right, and save the vectors

			float rowLerpValue = (float)r / (float)meshResY;

			bottomLeft.x = pos.x; bottomLeft.y = pos.x + dims.y; bottomLeft.z = 0f; //TODO try to use Set() ?  it doesn't always work.
			topRight.x = pos.x + dims.x; topRight.y = pos.y; topRight.z =  0f;
			bottomRight.Set(pos.x + dims.x, pos.y + dims.y, 0f);

			cellLeft = Vector3.Lerp(pos, bottomLeft, rowLerpValue); //lerp between topleft/bottomleft
			cellRight = Vector3.Lerp(topRight, bottomRight, rowLerpValue); //lerp between topright/bottomright

			for (int c = 0; c <= meshResX; c++)
			{
				//Now that we have our start and end coordinates for the row, iteratively lerp between them to get the "columns"
				float columnLerpValue = (float)c / (float)meshResX;

				//now get the final lerped vector
				Vector3 lerpedVector = Vector3.Lerp(cellLeft, cellRight, columnLerpValue);
				verts[v] = lerpedVector;

				//uvs
				//uvs.Add(new Vector2((float)c / (float)xTesselation, (float)r / yTesselation)); //0-1 code
				uv.x = (float)c / (float)meshResX;
				uv.y = (float)r / (float)meshResY;

				uv.x *= UVdims.x;
				uv.y *= UVdims.y;
				uv += UVpos;
				uvs[v] = uv;

				normals[v] = Vector3.forward;


				//David Lycan - When the Graphics Shader Level does not support Geometry shaders add the extra vertices, uvs and normals necessary
				//				Also add triangles to form each billboard quad here
				if (graphicsShaderLevel == 1)
				{
					if (v + 1 >= verts.Count) 
					{
						//Debug.LogWarning ("Array sizes were not properly calculated in updateSettings()!?");
						return;
					}
					verts[v + 1] = lerpedVector;
					verts[v + 2] = lerpedVector;
					verts[v + 3] = lerpedVector;
					
					uvs[v + 1] = uv;
					uvs[v + 2] = uv;
					uvs[v + 3] = uv;
					
					uv2s[v] = 	  new Vector2( 1f,  1f);
					uv2s[v + 1] = new Vector2(-1f,  1f);
					uv2s[v + 2] = new Vector2( 1f, -1f);
					uv2s[v + 3] = new Vector2(-1f, -1f);
					
					normals[v + 1] = Vector3.forward;
					normals[v + 2] = Vector3.forward;
					normals[v + 3] = Vector3.forward;

					v += 3;
				}
				v++;
			}
		}


		int t = 0;
		//we only want < gridunits because the very last verts in bth directions don't need triangles drawn for them.
		//David Lycan - When the Graphics Shader Level does support Geometry shaders or the default Holovid shader is being used then add triangles to form the video geometry here

		if (graphicsShaderLevel == 1)
		{
			for (int x = 0; x < meshResX; x++)
			{
				for (int y = 1; y <= meshResY; y++)
				{
					triangles[t] = x * 4 + y * (meshResX + 1) * 4 + 1; t++;
					triangles[t] = x * 4 + y * (meshResX + 1) * 4 + 2; t++;
					triangles[t] = x * 4 + y * (meshResX + 1) * 4; t++;
					
					triangles[t] = x * 4 + y * (meshResX + 1) * 4 + 1; t++;
					triangles[t] = x * 4 + y * (meshResX + 1) * 4 + 3; t++;
					triangles[t] = x * 4 + y * (meshResX + 1) * 4 + 2; t++;
					
				}
			}
		}
		else
		{
			for (int x = 0; x < meshResX; x++)
			{
				for (int y = 0; y < meshResY; y++)
				{
					triangles[t] = x + ((y + 1) * (meshResX + 1)); t++;
					triangles[t] = (x + 1) + (y * (meshResX + 1)); t++;
					triangles[t] = x + (y * (meshResX + 1)); t++; //width in verts

					if (graphicsShaderLevel != 2)
					{
						triangles[t] = x + ((y + 1) * (meshResX + 1)); t++;
						triangles[t] = (x + 1) + (y + 1) * (meshResX + 1); t++;
						triangles[t] = (x + 1) + (y * (meshResX + 1)); t++;
					}
				}
			}
		}
		

		//now that we have the mesh ready to go lets put it in
		MeshFilter meshFilter = g.GetComponent<MeshFilter>();
		if (!meshFilter)
			meshFilter = g.AddComponent<MeshFilter> ();
		meshFilter.sharedMesh.Clear ();
		//David Lycan - Set Mesh method is now based on the Unity version
		#if UNITY_VERSION_PRE_5_2
			meshFilter.vertices = verts.ToArray();
			meshFilter.triangles = triangles.ToArray();
			meshFilter.uv = uvs.ToArray();
			if (graphicsShaderLevel == 1)
			{
				meshFilter.uv2 = uv2s.ToArray();
			}
			meshFilter.normals = normals.ToArray();
		#else
			meshFilter.sharedMesh.SetVertices(verts);
			meshFilter.sharedMesh.SetTriangles(triangles, 0, false); //don't recalculate bounds
			meshFilter.sharedMesh.SetUVs(0, uvs);
			if (graphicsShaderLevel == 1)
			{
				meshFilter.sharedMesh.SetUVs(1, uv2s);
			}
			meshFilter.sharedMesh.SetNormals(normals);
		#endif

		//HACK ALERT!  just throw the bounds out very large, don't bother with precise calculations.
		//the autocalculate doesn't work because the shader is the one that can move the verts around
		//farther than the engine is expecting.
		meshFilter.sharedMesh.bounds = new Bounds (Vector3.zero, new Vector3 (200f, 200f, 200f));
	}
}
