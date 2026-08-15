using UnityEngine;
using System.Collections.Generic;

public class BlizzardManager : MonoBehaviour
{
    private ParticleSystem ps;
    private List<ParticleSystem.Particle> enterParticles = new List<ParticleSystem.Particle>();

    [SerializeField] PlayerController playerController;

    void Start()
    {
        ps = GetComponent<ParticleSystem>();
    }

    private void OnParticleTrigger()
    {
        // 1. Get all particles that just entered a trigger zone
        int numEnter = ps.GetTriggerParticles(ParticleSystemTriggerEventType.Enter, enterParticles);

        // 2. Loop through each particle that made contact
        for (int i = 0; i < numEnter; i++)
        {
            ParticleSystem.Particle p = enterParticles[i];

            // 3. Get the specific Collider component the particle hit
            Component triggerComponent = ps.trigger.GetCollider(0); 
            
            if (triggerComponent != null)
            {
                GameObject hitObject = triggerComponent.gameObject;
                if (hitObject.CompareTag("Player"))
                {
                    playerController.touchingSnow = true;
                }
            }
        }
    }
}
