using System.Collections;
using System.Collections.Generic;
using UnityEngine;


/// <summary>
/// Example character controller class
/// </summary>
public class SimpleCharacterController : MonoBehaviour
{

    public Camera m_camera;
    public CharacterController m_character;
    public float moveSpeed = 3f;
    public float runSpeed = 12f;

    public float rotationSpeed = 45f;

    private void Start() {
        Cursor.lockState = CursorLockMode.Locked;
    }

    void Update()
    {
        LookLogic();
        MotionLogic();
    }

    void MotionLogic()
    {
        Vector3 moveDirrection = new Vector3(Input.GetAxis("Horizontal"), 0f , Input.GetAxis("Vertical"));
        Vector3 motionVec = transform.TransformDirection(moveDirrection);

        float targetSpeed = moveSpeed;

        if (Input.GetKey(KeyCode.LeftShift)) targetSpeed = runSpeed;
        
        m_character.Move(motionVec.normalized * targetSpeed * Time.deltaTime);
    }


    float xRotation;
    void LookLogic()
    {
        Quaternion horizontalRotation = Quaternion.AngleAxis(rotationSpeed * Time.deltaTime * Input.GetAxis("Mouse X"), Vector3.up);
        transform.rotation = transform.rotation * horizontalRotation;

        xRotation += Input.GetAxis("Mouse Y") * Time.deltaTime * -rotationSpeed;
        if (xRotation > 75f) xRotation = 75f;
        if (xRotation < -90) xRotation = -90f;

        Vector3 euler = m_camera.transform.rotation.eulerAngles;
        euler.x = xRotation;
        m_camera.transform.rotation  = Quaternion.Euler(euler);

    }
}