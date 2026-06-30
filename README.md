Esse projeto foi desenvolvido como parte do Trabalho de Conclusão de Curso do Bacharelado em Ciência da Computação (UFABC - 02/2017)

Com ele é possível observar o comportamento de diferentes algoritmos de geração e resolução de labirintos, além de obter algumas informações com relação as características dos labirintos criados.

x--------------x--------------x--------------x--------------x--------------x--------------x--------------x--------------x

This project was developed as part of the Bachelor's Degree in Computer Science (UFABC - 02/2017)

With it, it is possible to observe the behavior of different maze generation and resolution algorithms, in addition to obtaining some information regarding the characteristics of the created mazes.

x--------------x--------------x--------------x--------------x--------------x--------------x--------------x--------------x

## 2026 modernization — Unity 6 LTS + DOTS

The project under `MazeAlgorithm/` was rebuilt from the original Unity 2018 / `MonoBehaviour` codebase to
**Unity 6 LTS (6000.0.78f1)** using the **Data-Oriented Technology Stack (DOTS)**:

- All six algorithms run as **Burst-compiled jobs** (Binary Tree, Recursive Backtracker, Eller; Wall Follower
  left/right, Dead-end Filling, Flood Fill/BFS).
- The maze is rendered with **Entities Graphics (ECS) instancing** — one entity per floor/wall — so it scales
  to very large grids instead of instantiating thousands of `GameObject`s.
- A single `MonoBehaviour` bootstrap builds a runtime UI and forwards commands to an ECS orchestrator system.

Open `MazeAlgorithm/` with Unity 6000.0.78f1 and play `Assets/Scenes/Maze.unity`. Implementation notes and the
headless build/verify commands are in [`MazeAlgorithm/CLAUDE.md`](MazeAlgorithm/CLAUDE.md).
