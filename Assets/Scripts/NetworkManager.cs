﻿using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine.Networking;
using UnityEngine.XR.iOS;
using UnityEngine;


/* NetworkManager manages synchronization of all the objects 
 * in the scene and communicating with the server
 */
public class NetworkManager : MonoBehaviour {
  
	/** Server endpoints **/ 
	public string address = "http://multiplayar.me";
	public string anchorEndpoint  = "/anchor";
	public string objectEndpoint = "/object";
	public string imageEndpoint = "/image";
	public string syncEndpoint = "/sync";

	public Dictionary<string, GameObject> objectMap = new Dictionary<string, GameObject>();
	public Dictionary<int, string> reverseObjectMap = new Dictionary<int, string> ();
	public GameObject mainCamera;
	public GameObject hitCubePrefab;
	public GameObject anchor;
	public Transform m_HitTransform;
	public List<Vector3> matchpoints;
	public List<GameObject> targets;
	public List<bool> targetselected;
	private Dictionary<Vector2, Vector3> hitPointMap = new Dictionary<Vector2, Vector3> ();
					
	private bool anchorSelect = false;
	private bool anchorChosen = false;

	private string guid;
	private string userId;
	private bool synced; 
	private GameObject newCube;
	private int frames;
	private bool lockPoints = false;

	private Sprite unselected;
	private Sprite selected;
	private Vector3[] pointClouds;

	private string pointformat = "{0:0.000},{1:0.000}";


	[System.Serializable] 
	public class PointInformation {
		public float x;
		public float y;

		public PointInformation(float x, float y) {
			this.x = x;
			this.y = y;
		}
	}

	[System.Serializable] 
	public class MatchedPoints {
		public List<PointInformation> points;

		public static MatchedPoints CreateFromJSON (string j) {
			return JsonUtility.FromJson<MatchedPoints> (j);
		}
	}

	[System.Serializable] 
	public class VectorInformation {
		public float x;
		public float y;
		public float z;
		public string id;
	}

//	[System.Serializable]
//	public class TransformData {
//		public List<VectorInformation> v;
//	}

	[System.Serializable]
	public class RootObject {
		public List<VectorInformation> users;
		public List<VectorInformation> objects;
	
		public static RootObject CreateFromJSON (string j) {
			return JsonUtility.FromJson<RootObject> (j);
		}
	}
		
	void Start () {
		Debug.Log("Network Manager started with a connection to " + address);
		userId = "";
		frames = 0;
		synced = false;
		guid = System.Guid.NewGuid().ToString();
		matchpoints = new List<Vector3> ();
		targets = new List<GameObject> ();
		SpriteRenderer unselectedR = GameObject.Find ("Unselected").GetComponent<SpriteRenderer> ();
		unselectedR.enabled = false;
		unselected = unselectedR.sprite;
		SpriteRenderer selectedR = GameObject.Find ("Selected").GetComponent<SpriteRenderer> ();
		selectedR.enabled = false;
		selected = selectedR.sprite;
		anchorSelect = true;
		targetselected = new List<bool> ();
		UnityARSessionNativeInterface.ARFrameUpdatedEvent += ARFrameUpdated;

	}

	public void ARFrameUpdated(UnityARCamera camera) {
		if (!lockPoints) {
			pointClouds = camera.pointCloudData;
		}
	}
	
	void Update () {
		frames++;
		if (frames % 10 == 0) {
			SyncWorld ();
			frames = 0;
		}

		if (!anchorSelect) {
			foreach (GameObject t in targets) {
//				Vector3 screenpos = mainCamera.GetComponentInChildren<Camera> ().WorldToScreenPoint(pos);
//				int dim = 64;
//				float x = screenpos.x;
//				float y = screenpos.y;
//				GUI.DrawTexture (new Rect(x, y, dim, dim), unselected, ScaleMode.ScaleToFit, true);
				t.transform.LookAt(mainCamera.transform, -Vector3.up);
			}

			if (Input.touchCount > 0 && Input.GetTouch (0).phase == TouchPhase.Began) 
			{
				Ray ray = Camera.main.ScreenPointToRay( Input.GetTouch(0).position );
				RaycastHit hit;
			
//				&& hit.transform.gameObject.name.StartsWith("target")

				if ( Physics.Raycast(ray, out hit) && hit.transform.gameObject.name.StartsWith("target") )
				{
					int index; 
					int.TryParse (hit.transform.gameObject.name.Substring(6), out index);
					Debug.Log (hit.transform.gameObject.name.Substring (6));
					if (targetselected [index]) {
						hit.transform.gameObject.GetComponent<SpriteRenderer> ().sprite = unselected;
						targetselected [index] = false;
					} else {
						hit.transform.gameObject.GetComponent<SpriteRenderer> ().sprite = selected; 
						anchorChosen = true;
						anchor = hit.transform.gameObject;
						SendOffset ();
						// TODO: unselect the rest
						for (int i = 0; i < targets.Count; i++) {
							GameObject other = targets [i];
							if (i != index) {
								other.SetActive (false);
							}
						}
						targetselected [index] = true;
					}

				}

			}
		}
	}

		 
	/* Each GameObject has an associated server-assigned string objectId. 
	 * The reverseObjectMap maps instanceId to objectId, and is thus used 
	 * to fetch the objectId for a GameObject. Note: empty string should 
	 * only be returned when the GameObject is first created.
	 */
	public string GetObjectId (GameObject obj) {
		if(reverseObjectMap.ContainsKey(obj.GetInstanceID())) {
			return reverseObjectMap[obj.GetInstanceID()];
		} else {
			return "";
		}
	}


	/* Retrieves the input GameObject's offset from the world anchor
	 */
	private Vector3 OffsetFromAnchor(GameObject obj) {
		Vector3 anchorPos = anchor.transform.position;
		Vector3 objPos = obj.transform.position;
		Vector3 offset = anchorPos - objPos;
		return offset;
	}

	public void testScreenshot() {
		Debug.Log ("Sending a new screenshot!");
		StartCoroutine (captureCameraView());
	}



	public IEnumerator captureCameraView() {


		int height = Screen.height;
		int width = Screen.width;

		Camera tempCam = mainCamera.GetComponentInChildren<Camera>();
		for (int i = 0; i < Mathf.Min (4, pointClouds.Length); i++) {
			Vector3 testPoint = pointClouds [i];
			Vector3 screenPos = tempCam.WorldToScreenPoint (testPoint);
			hitPointMap.Add (new Vector2(screenPos.x, screenPos.y), testPoint);
			PointInformation fakePoint = new PointInformation(screenPos.x, screenPos.y);
			Debug.Log ("making a fake point");
			drawServerPoint (fakePoint, i);
			anchorSelect = false;
		}
		yield break;

		// create a texture to render the camera's view to
		RenderTexture texture = new RenderTexture (width, height, 24);

		// alternatively use textures from the main camera
		Camera cam = mainCamera.GetComponentInChildren<Camera>();
		int prevMask = cam.cullingMask;
		cam.cullingMask = 0;
		RenderTexture prevTexture = cam.targetTexture;
		cam.targetTexture = texture;
		cam.Render ();

		// read the texture into a static texture2D
		RenderTexture.active = texture; // make the global render texture this one
		Texture2D savedTexture = new Texture2D(width, height, TextureFormat.RGB24, false);
		savedTexture.ReadPixels (new Rect (0, 0, width, height), 0, 0);
		savedTexture.Apply();
		RenderTexture.active = null; // release the static reference

		// once done, restore the main camera
		cam.cullingMask = prevMask;
		cam.targetTexture = prevTexture;

		// send this image to a server
		Debug.Log("Sending to the server");
//		WWWForm form = new WWWForm();
		byte[] raw_image_bytes;
		raw_image_bytes = savedTexture.EncodeToPNG();
		Debug.Log ("Writing " + raw_image_bytes.Length);
//		form.AddBinaryData ("image", raw_image_bytes);
		Dictionary<string, string> headers = new Dictionary<string, string> ();
		headers.Add ("x-user-id", guid);
		headers.Add ("x-dummy-id", "fucapplr");
		headers.Add ("x-points", serializeARKitPoints());

//		form.AddField("image", System.Convert.ToBase64String(raw_image_bytes));
//		form.AddField ("id", "1"); 
		WWW w = new WWW (address + imageEndpoint, raw_image_bytes, headers);
		Debug.Log("Sent the request");
		yield return w; 

		Debug.Log ("Response has returned");
		if (w.error != null) {
			Debug.Log (w.error);
		} else {
			Debug.Log ("IMAGE POINTS RETRIEVED FROM THE SERVER");
			MatchedPoints points = MatchedPoints.CreateFromJSON (w.text);
			Debug.Log (points.points.Count);
			int i = 0;
			foreach (PointInformation point in points.points) {
				Debug.Log ("POINT x: " + point.x + " y: " + point.y);
				drawServerPoint (point, i);
				i++;
			}
		}
	}

	public string serializeARKitPoints() {
		lockPoints = true;
		string serialized = "";
		Camera cam = mainCamera.GetComponentInChildren<Camera> ();
		foreach (Vector3 point in pointClouds) {
			Vector3 screenPos = cam.WorldToScreenPoint (point);
//			string key = string.Format (pointformat, screenPos.x, screenPos.y);
			serialized += string.Format ("{0:0.000},{1:0.000};", screenPos.x, screenPos.y);
			hitPointMap.Add (new Vector2(screenPos.x, screenPos.y), screenPos);
		}
		lockPoints = false;
		return serialized.Substring(0, serialized.Length - 1);
	}


	public void drawServerPoint(PointInformation point, int index) {
		Vector2 pointVector = new Vector2 (point.x, point.y);
//		string key = string.Format (pointformat, pointVector.x, pointVector.y);
		Vector3 anchorposition; 

		foreach (KeyValuePair<Vector2, Vector3> val in hitPointMap) {
			Vector2 mapPoint = val.Key;
			bool matched = false;
			if (mapPoint.Equals (pointVector)) {
				matched = true;
			}

			if (matched) {
				Debug.Log ("Found point!");
				anchorposition = val.Value;
				Debug.Log (anchorposition.ToString ());
				matchpoints.Add (anchorposition);
				GameObject target = new GameObject ();
				target.name = "target" + index;
				SpriteRenderer r = target.AddComponent<SpriteRenderer> ();
				target.AddComponent<BoxCollider> ();
				targetselected.Add (false);
				r.sprite = unselected;
				r.enabled = true;
				target.transform.position = anchorposition;
				target.transform.localScale = new Vector3 (0.05f, 0.05f, 0.05f);
				targets.Add (target);
				break;
			}
		}

//		if (hitPointMap.TryGetValue (key, out anchorposition)) {
//			matchpoints.Add (anchorposition);
//			GameObject target = new GameObject ();
//			target.name = "target" + index;
//			SpriteRenderer r = target.AddComponent<SpriteRenderer> ();
//			target.AddComponent<BoxCollider> ();
//			targetselected.Add (false);
//			r.sprite = unselected;
//			r.enabled = true;
//			target.transform.position = anchorposition;
//			target.transform.localScale = new Vector3 (0.02f, 0.02f, 0.02f);
//			targets.Add (target);
//		} else {
//			Debug.Log ("didn't find any matches in the hitpointmap");
//			foreach (KeyValuePair<string, Vector3> val in hitPointMap) {
//				Debug.Log (val.Key);
//				Debug.Log (key.Equals (val.Key));
//			}
//		}

		return;
	}
//		var screenPosition = Camera.main.ScreenToViewportPoint(pointVector);
//		ARPoint arPoint = new ARPoint {
//			x = screenPosition.x,
//			y = screenPosition.y
//		};
//
//		// prioritize reults types
//		ARHitTestResultType[] resultTypes = {
//			ARHitTestResultType.ARHitTestResultTypeExistingPlaneUsingExtent, 
//			// if you want to use infinite planes use this:
//			//ARHitTestResultType.ARHitTestResultTypeExistingPlane,
//			ARHitTestResultType.ARHitTestResultTypeHorizontalPlane, 
//			ARHitTestResultType.ARHitTestResultTypeFeaturePoint
//		}; 
//
//		foreach (ARHitTestResultType resultType in resultTypes)
//		{
//			if (HitTestWithResultType (arPoint, resultType, index))
//			{
//				return;
//			}
//		}
		// if it failed, try again
//		drawServerPoint (point);
//	}

//	bool HitTestWithResultType (ARPoint point, ARHitTestResultType resultTypes, int index)
//	{
//		List<ARHitTestResult> hitResults = UnityARSessionNativeInterface.GetARSessionNativeInterface ().HitTest (point, resultTypes);
//		Debug.Log ("FAKE NEWS");
//		Debug.Log (hitResults.Count);
//		if (hitResults.Count > 0) {
//			foreach (var hitResult in hitResults) {
////				m_HitTransform.position = UnityARMatrixOps.GetPosition (hitResult.worldTransform);
////				m_HitTransform.rotation = UnityARMatrixOps.GetRotation (hitResult.worldTransform);
//				Vector3 position = UnityARMatrixOps.GetPosition (hitResult.worldTransform);
//				matchpoints.Add (position);
//				GameObject target = new GameObject ();
//				target.name = "target" + index;
//				SpriteRenderer r = target.AddComponent<SpriteRenderer> ();
//				target.AddComponent<BoxCollider> ();
//				targetselected.Add (false);
//				r.sprite = unselected;
//				r.enabled = true;
//				target.transform.position = position;
////				target.transform.localRotation = Quaternion.LookRotation (mainCamera.transform.position - position);
//				target.transform.localScale = new Vector3 (0.02f, 0.02f, 0.02f);
//				targets.Add (target);
////				Debug.Log (string.Format ("FAKE POINT x:{0:0.######} y:{1:0.######} z:{2:0.######}", m_HitTransform.position.x, m_HitTransform.position.y, m_HitTransform.position.z));
//				return true;
//			}
//		}
//		return false;
//	}

  
	/*
	 */
	public void SendOffset() {
		if (synced) {
			return;
		}

		Vector3 offset = OffsetFromAnchor (mainCamera);

//		Debug.Log (string.Format ("Offset: x:{0:0.######} y:{1:0.######} z:{2:0.######}", offset.x, offset.y, offset.z));
//		Debug.Log (offset);

		WWWForm form = new WWWForm();
		form.AddField("x", offset.x.ToString());
		form.AddField("y", offset.y.ToString());
		form.AddField("z", offset.z.ToString());
		if (userId.Length != 0) {
			form.AddField("userId", userId);
		}

		newCube = Instantiate (hitCubePrefab, new Vector3 (0, 0, 0), Quaternion.identity);
		newCube.SetActive (true);
		anchor.SetActive (false);

		TransformManager t = newCube.AddComponent<TransformManager> ();
		t.nm = this;

		synced = true;

		WWW www = new WWW(address + anchorEndpoint, form);
		StartCoroutine(WaitForUserId(www));
	}

	/* Method is called if the user added an object to the environment 
	 * OR if an object's position has changed. Not sending an ObjectId 
	 * signals the server that this is a brand new object.
	 */
	public void SendObject(string objectId, GameObject obj) {

//		Vector3 offset = OffsetFromAnchor (obj);
		GameObject temp = new GameObject ();
		temp.transform.position = obj.transform.position;
		temp.transform.RotateAround (anchor.transform.position, Vector3.up, -1.0f * anchor.transform.rotation.eulerAngles.y);
		temp.transform.position = temp.transform.position - anchor.transform.position;
		Vector3 offset = temp.transform.position;
//		Vector3 offset = obj.transform.position - anchor.transform.position;

		Debug.Log ("Sending update for Object: " + objectId);
		Debug.Log ("Local Position: " + obj.transform.position + " World Position: " + offset.ToString());

		WWWForm form = new WWWForm ();
		form.AddField("x", offset.x.ToString());
		form.AddField("y", offset.y.ToString());
		form.AddField("z", offset.z.ToString());
		if (objectId.Length != 0) {
			form.AddField("objectId", objectId);
		}

		WWW www = new WWW(address + objectEndpoint, form);
		StartCoroutine(WaitForObjectId(www, obj));
	}


	/* Get state of the world from server
	 */
	public void SyncWorld() {
		if (!anchorChosen) {
			return;
		}
		WWW www = new WWW (address + syncEndpoint);
		StartCoroutine (WaitForWorldSync (www));
	}
  
	/* Get userId from server
	 */
	public IEnumerator WaitForUserId(WWW www) {
		yield return www;

		if (www.error != null) {
			Debug.Log("WWW Error: "+ www.error);
			yield break;
		}

		userId = www.text;
	}

	/* If in SendObject() we pushed information about a brand new object,
	 * the server will respond with an objectId which we use to update our 
	 * objectMap and reverseObjectMap.
	 */
	public IEnumerator WaitForObjectId(WWW www, GameObject obj) {
		yield return www;
		
		if (www.error != null) {
			Debug.Log ("WWW Error: " + www.error);
			yield break;
		}
				
		string objectId = www.text;

		if (!objectMap.ContainsKey (objectId)) {
			objectMap.Add (objectId, obj);	
			reverseObjectMap.Add (obj.GetInstanceID (), objectId);
		} 
		TransformManager t = newCube.GetComponent<TransformManager> ();
		t.objectId = objectId;

	}

	/* Retrieves the server maintained state of the world,
	 * for known GameObjects, updates their positions. For new objects,
	 * places them into the world and then removes UnityARHitTestExample script
	 * which prevents the user from modifying them. This way, a user cannot move 
	 * objects created by another user.
	 */ 
	public IEnumerator WaitForWorldSync(WWW www) {
		yield return www;

		if (www.error != null) {
			Debug.Log ("WWW Error: " + www.error);
			yield break;
		}
			
		RootObject root = RootObject.CreateFromJSON(www.text);

		Debug.Log ("WORLD SYNC: " + root.objects.Count);

		foreach (VectorInformation v in root.objects) {
			string objectId = v.id;
			Vector3 otherPos = new Vector3 (v.x, v.y, v.z);



			if (objectMap.ContainsKey (objectId)) {
				GameObject obj = objectMap [objectId];

				Debug.Log ("World Sync Object: " + objectId + " to position: " + otherPos.ToString ());

				GameObject temp = new GameObject ();
				temp.transform.position = otherPos + anchor.transform.position;
				temp.transform.RotateAround (anchor.transform.position, Vector3.up, anchor.transform.rotation.eulerAngles.y);
				Vector3 newPos = temp.transform.position;

				obj.transform.position = newPos;
			} else {
				GameObject other = Instantiate (hitCubePrefab, otherPos + anchor.transform.position, Quaternion.identity);
				other.transform.RotateAround (anchor.transform.position, Vector3.up, anchor.transform.rotation.eulerAngles.y);

				objectMap.Add (objectId, other);
				reverseObjectMap.Add (other.GetInstanceID (), objectId);

				Component[] scripts = other.GetComponentsInChildren (typeof(UnityEngine.XR.iOS.UnityARHitTestExample), true);
				foreach (UnityEngine.XR.iOS.UnityARHitTestExample s in scripts) {
					Destroy (s);
				}

			}
		}
	}

}
