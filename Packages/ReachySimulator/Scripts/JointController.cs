using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class JointController : MonoBehaviour
{
    private float targetPosition;
    private ArticulationBody articulation;
    private float speedLimitPercent = 100f;
    private bool compliant = false;

    private const float MinSpeedDegPerSecond = 0.5f;
    private const float MaxSpeedDegPerSecond = 90f;
    private const float MinDriveStiffness = 50f;
    private const float MaxDriveStiffness = 1000f;

    void Start()
    {
        articulation = GetComponent<ArticulationBody>();
        if (articulation != null)
        {
            targetPosition = articulation.xDrive.target;
        }
    }

    void FixedUpdate()
    {
        if (articulation == null)
        {
            return;
        }

        var drive = articulation.xDrive;
        float normalized = Mathf.Clamp(speedLimitPercent, 1f, 100f) / 100f;
        float commandedSpeed = Mathf.Lerp(MinSpeedDegPerSecond, MaxSpeedDegPerSecond, normalized);
        float maxStep = commandedSpeed * Time.fixedDeltaTime;
        drive.target = Mathf.MoveTowards(drive.target, targetPosition, maxStep);
        drive.stiffness = compliant ? 0f : Mathf.Lerp(MinDriveStiffness, MaxDriveStiffness, normalized);
        articulation.xDrive = drive;
    }

    public void RotateTo(float newTargetPosition)
    {
        targetPosition = newTargetPosition;
    }

    public void IsCompliant(bool comp)
    {
        compliant = comp;
    }

    public void SetSpeedLimit(float speedPercent)
    {
        speedLimitPercent = Mathf.Clamp(speedPercent, 1f, 100f);
    }

    public float GetPresentPosition()
    {
        return Mathf.Rad2Deg * articulation.jointPosition[0];
    }
}
