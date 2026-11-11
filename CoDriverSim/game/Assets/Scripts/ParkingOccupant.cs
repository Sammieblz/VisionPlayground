using UnityEngine;
using UnityEngine.AI;

namespace CoDriver
{
    // A visible driver owns the parked car's departure handshake. Navigation and
    // seated animation meet at the open door; the car waits for boarding to finish.
    public sealed class ParkingOccupant : MonoBehaviour
    {
        Town town;
        Walker person;
        NavMeshAgent agent;
        RigMotion rig;
        Rigidbody body;
        int phase,
            seenVisits;
        float phaseAt;
        Vector3 from;
        Quaternion fromRotation;
        public bool OwnsNavigation => phase != 6;

        public void Initialize(
            Town world,
            Walker walker,
            NavMeshAgent navigator,
            RigMotion animation
        )
        {
            town = world;
            person = walker;
            agent = navigator;
            rig = animation;
            body = GetComponent<Rigidbody>();
            phaseAt = Time.time;
            Go(town.ParkingCar.DriverDoorPoint);
        }

        void Go(Vector3 point)
        {
            if (agent.enabled && agent.isOnNavMesh)
            {
                agent.isStopped = false;
                agent.SetDestination(point);
            }
        }

        void Change(int next)
        {
            phase = next;
            phaseAt = Time.time;
            from = body.position;
            fromRotation = body.rotation;
        }

        void SeatedPose(Vector3 position, Quaternion rotation)
        {
            // These phases have no world collider. Move the actual body so
            // interpolation cannot restore a stale seat pose when alighting.
            body.position = position;
            body.rotation = rotation;
        }

        void Update()
        {
            var car = town.ParkingCar;
            if (car.ScenarioOccupant && phase < 3)
            {
                agent.enabled = false;
                GetComponent<Collider>().enabled = false;
                Change(3);
                seenVisits = car.ParkingVisits;
            }
            switch (phase)
            {
                case 0:
                    person.State = "Walking to parked car";
                    rig?.Animate(person.ActualSpeed, "Walking", 0);
                    if (
                        agent.isOnNavMesh
                        && !agent.pathPending
                        && Vector3.Distance(transform.position, car.DriverDoorPoint) < .65f
                    )
                    {
                        agent.isStopped = true;
                        car.OccupantDoorOpen = true;
                        Change(1);
                    }
                    break;
                case 1:
                    person.State = "Opening car door";
                    rig?.Animate(0, person.State, 0);
                    if (Time.time - phaseAt > 1.3f)
                    {
                        agent.enabled = false;
                        GetComponent<Collider>().enabled = false;
                        Change(2);
                    }
                    break;
                case 2:
                    person.State = "Entering car";
                    float entry = Mathf.Clamp01((Time.time - phaseAt) / 1.8f);
                    SeatedPose(
                        Vector3.Lerp(from, car.DriverSeat, entry),
                        Quaternion.Slerp(fromRotation, car.transform.rotation, entry)
                    );
                    rig?.Animate(0, person.State, 0);
                    if (entry >= 1)
                    {
                        car.OccupantDoorOpen = false;
                        car.DriverReady = true;
                        seenVisits = car.ParkingVisits;
                        Change(3);
                    }
                    break;
                case 3:
                    person.State = "Driving parking vehicle";
                    SeatedPose(car.DriverSeat, car.transform.rotation);
                    rig?.Animate(0, "Driving", 0);
                    if (car.IsParked && car.ParkingVisits > seenVisits)
                    {
                        car.OccupantDoorOpen = true;
                        Change(4);
                    }
                    break;
                case 4:
                    person.State = "Opening car door";
                    if (Time.time - phaseAt > 1.3f)
                        Change(5);
                    break;
                case 5:
                    person.State = "Leaving car";
                    float exit = Mathf.Clamp01((Time.time - phaseAt) / 1.8f);
                    SeatedPose(
                        Vector3.Lerp(from, car.DriverDoorPoint, exit),
                        car.transform.rotation
                    );
                    rig?.Animate(0, person.State, 0);
                    if (exit >= 1)
                    {
                        agent.enabled = true;
                        agent.Warp(car.DriverDoorPoint);
                        GetComponent<Collider>().enabled = true;
                        car.OccupantDoorOpen = false;
                        Change(6);
                        person.VisitBuilding();
                    }
                    break;
                case 6:
                    if (!person.VisitingBuilding && Time.time - phaseAt > 2)
                    {
                        Change(0);
                        Go(car.DriverDoorPoint);
                    }
                    break;
            }
        }
    }
}
