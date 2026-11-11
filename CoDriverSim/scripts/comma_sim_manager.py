"""Configure only the simulator's card entry point, then run the stock manager."""

if __name__ == "__main__":
    from openpilot.system.manager.manager import main
    from openpilot.system.manager.process_config import managed_processes

    managed_processes["card"].module = "comma_sim_card"
    main()
