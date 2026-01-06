using UnityEngine;
using System.Collections;

public class TrainSimulationFlow : MonoBehaviour
{
    private Animator animator;
    public Transform player;
    private TCPReceiver tcpReceiver;

    void Start()
    {
        animator = GetComponent<Animator>();
        
        // Find the TCPReceiver in the scene
        tcpReceiver = FindObjectOfType<TCPReceiver>();
        
        if (tcpReceiver == null)
        {
            Debug.LogError("TCPReceiver not found! Make sure TCPReceiver script is attached to an object in the scene.");
        }
        
        StartCoroutine(SimulationSequence());
    }

    IEnumerator SimulationSequence()
    {
        // Wait before arrival
        yield return new WaitForSeconds(120f);

        // Train arrives
        animator.Play("TrainArrive");
        yield return WaitForAnimation("TrainArrive");

        // Doors open
        animator.Play("DoorsOpen");
        yield return WaitForAnimation("DoorsOpen");

        // BOARD PLAYER
        bool boardingSuccess = BoardPlayer();

        // Boarding time
        yield return new WaitForSeconds(30f);

        // Doors close
        animator.Play("DoorsClose");
        yield return WaitForAnimation("DoorsClose");

        // Train departs (player moves WITH train)
        animator.Play("TrainDeparture");
        yield return WaitForAnimation("TrainDeparture");

        // Determine final success state
        bool simulationSuccess = boardingSuccess && PlayerIsOnTrain();
        
        // Notify MATLAB through TCPReceiver
        NotifyMatlabEndState(simulationSuccess);
    }

    bool BoardPlayer()
    {
        if (player == null)
        {
            Debug.LogWarning("Player not found for boarding!");
            return false;
        }

        try
        {
            // Preserve world position and parent to train
            player.SetParent(transform, true);
            Debug.Log("Player successfully boarded the train");
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError("Failed to board player: " + e.Message);
            return false;
        }
    }

    bool PlayerIsOnTrain()
    {
        // Check if player is still parented to train
        if (player == null) return false;
        
        return player.parent == transform;
    }

    IEnumerator WaitForAnimation(string stateName)
    {
        yield return null;

        while (animator.GetCurrentAnimatorStateInfo(0).IsName(stateName) &&
               animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f)
        {
            yield return null;
        }
    }

    void NotifyMatlabEndState(bool success)
    {
        Debug.Log("SIMULATION ENDED: " + (success ? "SUCCESS" : "FAILURE"));
        
        // Send notification through TCPReceiver
        if (tcpReceiver != null)
        {
            tcpReceiver.NotifySimulationEnd(success);
        }
        else
        {
            Debug.LogError("Cannot notify MATLAB - TCPReceiver not found!");
        }
    }

    // Optional: Manual testing methods (right-click in Inspector)
    [ContextMenu("Test Success Notification")]
    void TestSuccessNotification()
    {
        NotifyMatlabEndState(true);
    }

    [ContextMenu("Test Failure Notification")]
    void TestFailureNotification()
    {
        NotifyMatlabEndState(false);
    }

    // Optional: Trigger simulation end early for testing
    [ContextMenu("Force End Simulation (Success)")]
    void ForceEndSuccess()
    {
        StopAllCoroutines();
        NotifyMatlabEndState(true);
    }

    [ContextMenu("Force End Simulation (Failure)")]
    void ForceEndFailure()
    {
        StopAllCoroutines();
        NotifyMatlabEndState(false);
    }
}