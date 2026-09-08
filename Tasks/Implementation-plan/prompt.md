# Prepare implementation plan

I need an implementation plan for a life simulation game engine. Split implementation into milestones, user stories or even subtask if possible. Add prefixes in the names of user stories / subtask to reflect the type of the task: [CS] for console application and [AI] for any AI related tasks (agents, skills etc.). I want a detailed implementation plan. Consider all the details and edge cases. The plan should be detailed enough that I could use it to implement the game engine. The application will run locally only. I do not want to deploy it anywhere.

## High level vision

I want to create a life simulation game engine that will be able to consume markdown files describing specific game world characteristics and provide a way to play the game using those characteristics. The mechanism (provided by the game engine) for each game should be similar but scenarios could be different. That's why I think game engine is what I'm looking for.

### Example

I can think about to different games of a life simulation type, that are powered by the same game engine. For example there could be a game where you play as a human and your goal is to earn money, learn new skills, improve relationships with others, raise a family, etc.
There could be another game where you also play as a human but with different goals and different type of interactions with characters. Both games share similar general concepts like locations, actions, time, energy and general flow, turns based gameplay. The differences are mostly in the description of game world, characters, traits, etc. which are defined in markdown files.

## Solution Design

I want to make a C# (.NET 10) console app using Spectre.Console for a game engine.
The game engine should be responsible for controling the time (days of week, hours),
controlling the user stats (energy, hunger, mood, money, etc.) and game world state.
I want local AI agents to be able to interact with the game world.
AI agents should control NPCs and locations in the game world. They should generate description, dialogs and interactions. One of agents should be able to generate answer / action options. Another agent should be responsible for translating AI answers into game actions. Than game engine should consume AI output to update the stats of user, npcs and world.
Locally I want to use Jan as a api server exposing local LLM. I want to use open code for multi-agent configuration. I'm not sure about C# open source libraries for this. I want to use the most simple tools possible to achieve the goal. Prefer ready solutions over custom made.

Game engine should consume markdown files to understand the game world. e could probably even think of some kind of package system for game worlds (similar to NuGet packages) where you could install different game worlds. Those packages could contain the markdown files describing diffrent aspects of the game world.
