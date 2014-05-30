﻿using System; using ComponentBind;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Lemma.Components;
using Lemma.Util;
using BEPUphysics.BroadPhaseEntries;
using BEPUphysics.CollisionTests;
using System.Threading;

namespace Lemma.Factories
{
	public class SnakeFactory : Factory<Main>
	{
		public override Entity Create(Main main)
		{
			return new Entity(main, "Snake");
		}

		public override void Bind(Entity entity, Main main, bool creating = false)
		{
			if (ParticleSystem.Get(main, "SnakeSparks") == null)
			{
				ParticleSystem.Add(main, "SnakeSparks",
				new ParticleSystem.ParticleSettings
				{
					TextureName = "Particles\\splash",
					MaxParticles = 1000,
					Duration = TimeSpan.FromSeconds(1.0f),
					MinHorizontalVelocity = -7.0f,
					MaxHorizontalVelocity = 7.0f,
					MinVerticalVelocity = 0.0f,
					MaxVerticalVelocity = 7.0f,
					Gravity = new Vector3(0.0f, -10.0f, 0.0f),
					MinRotateSpeed = -2.0f,
					MaxRotateSpeed = 2.0f,
					MinStartSize = 0.3f,
					MaxStartSize = 0.7f,
					MinEndSize = 0.0f,
					MaxEndSize = 0.0f,
					BlendState = Microsoft.Xna.Framework.Graphics.BlendState.AlphaBlend,
					MinColor = new Vector4(2.0f, 2.0f, 2.0f, 1.0f),
					MaxColor = new Vector4(2.0f, 2.0f, 2.0f, 1.0f),
				});
			}

			entity.CannotSuspendByDistance = true;
			Transform transform = entity.GetOrCreate<Transform>("Transform");

			Property<float> operationalRadius = entity.GetOrMakeProperty<float>("OperationalRadius", true, 100.0f);

			ListProperty<Voxel.Coord> path = entity.GetOrMakeListProperty<Voxel.Coord>("PathCoordinates");

			Property<Entity.Handle> targetAgent = entity.GetOrMakeProperty<Entity.Handle>("TargetAgent");

			AI ai = entity.GetOrCreate<AI>("AI");

			Agent agent = entity.GetOrCreate<Agent>("Agent");

			Voxel.State infectedState = Voxel.States[Voxel.t.Infected],
				neutralState = Voxel.States[Voxel.t.Neutral];

			const float defaultSpeed = 5.0f;
			const float chaseSpeed = 18.0f;
			const float closeChaseSpeed = 12.0f;
			const float crushSpeed = 125.0f;

			VoxelChaseAI chase = entity.GetOrCreate<VoxelChaseAI>("VoxelChaseAI");
			chase.Add(new TwoWayBinding<Vector3>(transform.Position, chase.Position));
			chase.Speed.Value = defaultSpeed;
			chase.Filter = delegate(Voxel.State state)
			{
				if (state == infectedState || state == neutralState)
					return VoxelChaseAI.Cell.Penetrable;
				return VoxelChaseAI.Cell.Avoid;
			};
			entity.Add(new CommandBinding(chase.Delete, entity.Delete));

			PointLight positionLight = null;
			Property<float> positionLightRadius = entity.GetOrMakeProperty<float>("PositionLightRadius", true, 20.0f);
			if (!main.EditorEnabled)
			{
				positionLight = new PointLight();
				positionLight.Serialize = false;
				positionLight.Color.Value = new Vector3(1.5f, 0.5f, 0.5f);
				positionLight.Add(new Binding<float>(positionLight.Attenuation, positionLightRadius));
				positionLight.Add(new Binding<bool, string>(positionLight.Enabled, x => x != "Suspended", ai.CurrentState));
				positionLight.Add(new Binding<Vector3, string>(positionLight.Color, delegate(string state)
				{
					switch (state)
					{
						case "Chase":
						case "Crush":
							return new Vector3(1.5f, 0.5f, 0.5f);
						case "Alert":
							return new Vector3(1.5f, 1.5f, 0.5f);
						default:
							return new Vector3(1.0f, 1.0f, 1.0f);
					}
				}, ai.CurrentState));
				entity.Add("PositionLight", positionLight);
				ParticleEmitter emitter = entity.GetOrCreate<ParticleEmitter>("Particles");
				emitter.Editable = false;
				emitter.Serialize = false;
				emitter.ParticlesPerSecond.Value = 100;
				emitter.ParticleType.Value = "SnakeSparks";
				emitter.Add(new Binding<Vector3>(emitter.Position, transform.Position));
				emitter.Add(new Binding<bool, string>(emitter.Enabled, x => x != "Suspended", ai.CurrentState));

				positionLight.Add(new Binding<Vector3>(positionLight.Position, transform.Position));
				emitter.Add(new Binding<Vector3>(emitter.Position, transform.Position));
				agent.Add(new Binding<Vector3>(agent.Position, transform.Position));
			}

			AI.Task checkMap = new AI.Task
			{
				Action = delegate()
				{
					if (chase.Voxel.Value.Target == null || !chase.Voxel.Value.Target.Active)
						entity.Delete.Execute();
				},
			};

			AI.Task checkOperationalRadius = new AI.Task
			{
				Interval = 2.0f,
				Action = delegate()
				{
					bool shouldBeActive = (transform.Position.Value - main.Camera.Position).Length() < operationalRadius;
					if (shouldBeActive && ai.CurrentState == "Suspended")
						ai.CurrentState.Value = "Idle";
					else if (!shouldBeActive && ai.CurrentState != "Suspended")
						ai.CurrentState.Value = "Suspended";
				},
			};

			AI.Task checkTargetAgent = new AI.Task
			{
				Action = delegate()
				{
					Entity target = targetAgent.Value.Target;
					if (target == null || !target.Active)
					{
						targetAgent.Value = null;
						ai.CurrentState.Value = "Idle";
					}
				},
			};

			chase.Add(new CommandBinding<Voxel, Voxel.Coord>(chase.Moved, delegate(Voxel m, Voxel.Coord c)
			{
				if (chase.Active)
				{
					string currentState = ai.CurrentState.Value;
					if ((currentState == "Chase" || currentState == "Crush"))
					{
						bool regenerate = m.Empty(c);
						regenerate |= m.Fill(c, infectedState);
						if (regenerate)
							m.Regenerate();
					}
					AkSoundEngine.PostEvent("Play_snake_move", entity);

					if (path.Count > 0)
					{
						chase.Coord.Value = path[0];
						path.RemoveAt(0);
					}
				}
			}));

			Property<Voxel.Coord> crushCoordinate = entity.GetOrMakeProperty<Voxel.Coord>("CrushCoordinate");

			ai.Setup
			(
				new AI.AIState
				{
					Name = "Suspended",
					Tasks = new[] { checkOperationalRadius },
				},
				new AI.AIState
				{
					Name = "Idle",
					Tasks = new[]
					{
						checkMap,
						checkOperationalRadius,
						new AI.Task
						{
							Interval = 1.0f,
							Action = delegate()
							{
								Agent a = Agent.Query(transform.Position, 50.0f, 20.0f, x => x.Entity.Type == "Player");
								if (a != null)
									ai.CurrentState.Value = "Alert";
							},
						},
					},
				},
				new AI.AIState
				{
					Name = "Alert",
					Enter = delegate(AI.AIState previous)
					{
						chase.EnableMovement.Value = false;
					},
					Exit = delegate(AI.AIState next)
					{
						chase.EnableMovement.Value = true;
					},
					Tasks = new[]
					{
						checkMap,
						checkOperationalRadius,
						new AI.Task
						{
							Interval = 1.0f,
							Action = delegate()
							{
								if (ai.TimeInCurrentState > 3.0f)
									ai.CurrentState.Value = "Idle";
								else
								{
									Agent a = Agent.Query(transform.Position, 50.0f, 30.0f, x => x.Entity.Type == "Player");
									if (a != null)
									{
										targetAgent.Value = a.Entity;
										ai.CurrentState.Value = "Chase";
									}
								}
							},
						},
					},
				},
				new AI.AIState
				{
					Name = "Chase",
					Enter = delegate(AI.AIState previousState)
					{
						chase.TargetActive.Value = true;
						chase.Speed.Value = chaseSpeed;
					},
					Exit = delegate(AI.AIState nextState)
					{
						chase.TargetActive.Value = false;
						chase.Speed.Value = defaultSpeed;
					},
					Tasks = new[]
					{
						checkMap,
						checkOperationalRadius,
						checkTargetAgent,
						new AI.Task
						{
							Interval = 0.07f,
							Action = delegate()
							{
								Vector3 targetPosition = targetAgent.Value.Target.Get<Agent>().Position;

								float targetDistance = (targetPosition - transform.Position).Length();

								chase.Speed.Value = targetDistance < 15.0f ? closeChaseSpeed : chaseSpeed;

								if (targetDistance > 50.0f || ai.TimeInCurrentState > 40.0f) // He got away
									ai.CurrentState.Value = "Alert";
								else if (targetDistance < 5.0f) // We got 'im
									ai.CurrentState.Value = "Crush";
								else
									chase.Target.Value = targetPosition;
							},
						},
					},
				},
				new AI.AIState
				{
					Name = "Crush",
					Enter = delegate(AI.AIState lastState)
					{
						// Set up cage
						Voxel.Coord center = chase.Voxel.Value.Target.Get<Voxel>().GetCoordinate(targetAgent.Value.Target.Get<Agent>().Position);

						int radius = 1;

						// Bottom
						for (int x = center.X - radius; x <= center.X + radius; x++)
						{
							for (int z = center.Z - radius; z <= center.Z + radius; z++)
								path.Add(new Voxel.Coord { X = x, Y = center.Y - 4, Z = z });
						}

						// Outer shell
						radius = 2;
						for (int y = center.Y - 3; y <= center.Y + 3; y++)
						{
							// Left
							for (int z = center.Z - radius; z <= center.Z + radius; z++)
								path.Add(new Voxel.Coord { X = center.X - radius, Y = y, Z = z });

							// Right
							for (int z = center.Z - radius; z <= center.Z + radius; z++)
								path.Add(new Voxel.Coord { X = center.X + radius, Y = y, Z = z });

							// Backward
							for (int x = center.X - radius; x <= center.X + radius; x++)
								path.Add(new Voxel.Coord { X = x, Y = y, Z = center.Z - radius });

							// Forward
							for (int x = center.X - radius; x <= center.X + radius; x++)
								path.Add(new Voxel.Coord { X = x, Y = y, Z = center.Z + radius });
						}

						// Top
						for (int x = center.X - radius; x <= center.X + radius; x++)
						{
							for (int z = center.Z - radius; z <= center.Z + radius; z++)
								path.Add(new Voxel.Coord { X = x, Y = center.Y + 3, Z = z });
						}

						chase.EnablePathfinding.Value = false;
						chase.Speed.Value = crushSpeed;

						crushCoordinate.Value = chase.Coord;
					},
					Exit = delegate(AI.AIState nextState)
					{
						chase.EnablePathfinding.Value = true;
						chase.Speed.Value = defaultSpeed;
						chase.Coord.Value = chase.LastCoord.Value = crushCoordinate;
						path.Clear();
					},
					Tasks = new[]
					{
						checkMap,
						checkOperationalRadius,
						checkTargetAgent,
						new AI.Task
						{
							Interval = 0.01f,
							Action = delegate()
							{
								Agent a = targetAgent.Value.Target.Get<Agent>();
								a.Health.Value -= 0.01f / 1.5f; // seconds to kill
								if (!a.Active)
									ai.CurrentState.Value = "Alert";
								else
								{
									if ((a.Position - transform.Position.Value).Length() > 5.0f) // They're getting away
										ai.CurrentState.Value = "Chase";
								}
							}
						}
					},
				}
			);

			this.SetMain(entity, main);
		}
	}
}
