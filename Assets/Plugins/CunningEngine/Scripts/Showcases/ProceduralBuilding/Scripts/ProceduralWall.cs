using System.Collections;
using System.Collections.Generic;
using UnityEngine;


[ExecuteInEditMode]
public class ProceduralWall : MonoBehaviour {

    public Vector3 boundsSize;
    public MeshFilter m_meshfilter;


    public float validateValue = 0f;
	// Use this for initialization
	void Start () {
		
	}
	
	// Update is called once per frame
	void Update () {
        Debug.DrawRay(transform.position, -transform.right * boundsSize.x, Color.red);
        Debug.DrawRay(transform.position, transform.up * boundsSize.y, Color.green);
        Debug.DrawRay(transform.position, -transform.forward * boundsSize.z, Color.blue);


    }

    public float boundsMultiply = 1f;

    private void OnValidate()
    {
        m_meshfilter = GetComponent<MeshFilter>();
        m_meshfilter.sharedMesh.RecalculateBounds();
        Mesh m = m_meshfilter.sharedMesh;
        m.RecalculateBounds();

         

        boundsSize = m.bounds.size * boundsMultiply;

        

        Debug.Log( m_meshfilter.sharedMesh.bounds.ToString());
    }
}
